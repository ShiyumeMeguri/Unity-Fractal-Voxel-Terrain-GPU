// Assets/ScriptsECS/Meshing/MeshJobHandler.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptIn.Voxel.Meshing
{
    public class MeshJobHandler
    {
        public struct Stats
        {
            public bool IsEmpty;
            public Bounds Bounds;
            public int VertexCount;
            public int MainMeshIndexCount;
            public int[] ForcedSkirtFacesTriCount;
            public Vertices Vertices;
            public NativeArray<int> MainMeshIndices;
        }

        private CoreMeshHandler _Core;
        private SkirtHandler _Skirt;
        private MergeMeshHandler _Merger;
        private ApplyMeshHandler _Apply;
        private NormalsHandler _Normals;

        private JobHandle _JobHandle;
        private Entity _Entity;

        public MeshJobHandler()
        {
            _Core = new CoreMeshHandler();
            _Skirt = new SkirtHandler();
            _Merger = new MergeMeshHandler();
            _Apply = new ApplyMeshHandler();
            _Normals = new NormalsHandler();
        }

        public void Init(TerrainConfig config)
        {
            _Core.Init(config);
            _Skirt.Init(config);
            _Merger.Init(config);
            _Apply.Init(config);
            _Normals.Init(config);
        }

        public bool IsFree { get; private set; } = true;

        public bool IsComplete(EntityManager manager) => _JobHandle.IsCompleted && !IsFree && manager.Exists(_Entity);

        public void BeginJob(Entity entity, ref TerrainChunkVoxels chunkVoxels, EntityManager mgr, JobHandle dependency)
        {
            IsFree = false;
            _Entity = entity;

            var deps = new NativeArray<JobHandle>(4, Allocator.Temp);
            deps[0] = dependency;
            deps[1] = _JobHandle;
            deps[2] = chunkVoxels.AsyncReadJobHandle;
            deps[3] = chunkVoxels.AsyncWriteJobHandle;
            JobHandle currentHandle = JobHandle.CombineDependencies(deps);
            deps.Dispose();

            _Normals.Schedule(ref chunkVoxels, currentHandle);
            currentHandle = _Normals.JobHandle;

            _Core.Schedule(ref chunkVoxels, ref _Normals, currentHandle);

            _Skirt.Schedule(ref chunkVoxels, ref _Core, ref _Normals, _Core.JobHandle);

            _Merger.Schedule(ref _Core, ref _Skirt);

            _Apply.Schedule(ref _Merger);

            _JobHandle = _Apply.JobHandle;
            chunkVoxels.AsyncReadJobHandle = _JobHandle;
        }

        public bool TryComplete(EntityManager mgr, out Mesh outMesh, out Entity outEntity, out Stats outStats)
        {
            _JobHandle.Complete();
            IsFree = true;

            outMesh = null;
            outEntity = Entity.Null;
            outStats = default;

            if (!mgr.Exists(_Entity)) return false;

            outEntity = _Entity;
            bool isEmpty = _Merger.TotalVertexCount.Value == 0;

            outStats = new Stats
            {
                Bounds = new Bounds { min = _Apply.Bounds.Value.Min, max = _Apply.Bounds.Value.Max },
                VertexCount = _Merger.TotalVertexCount.Value,
                MainMeshIndexCount = _Merger.SubmeshIndexCounts[0],
                ForcedSkirtFacesTriCount = _Skirt.ForcedTriangleCounter.ToArray(),
                IsEmpty = isEmpty,
                Vertices = _Merger.MergedVertices,
                MainMeshIndices = _Merger.MergedIndices,
            };

            if (isEmpty)
            {
                // [修复] Mesh.MeshDataArray 没有 IsCreated 属性，直接 Dispose
                _Apply.MeshDataArray.Dispose();
            }
            else
            {
                outMesh = new Mesh { name = "VoxelChunkMesh", indexFormat = IndexFormat.UInt32 };
                Mesh.ApplyAndDisposeWritableMeshData(_Apply.MeshDataArray, outMesh, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            }

            return true;
        }

        public void Dispose()
        {
            _JobHandle.Complete();
            _Core.Dispose();
            _Skirt.Dispose();
            _Merger.Dispose();
            _Apply.Dispose();
            _Normals.Dispose();
        }
    }
}