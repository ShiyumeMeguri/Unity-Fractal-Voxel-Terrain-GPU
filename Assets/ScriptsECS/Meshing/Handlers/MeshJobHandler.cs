// Meshing/Handlers/MeshJobHandler.cs
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

        public MeshJobHandler(TerrainConfig config)
        {
            _Core = new CoreMeshHandler();
            _Skirt = new SkirtHandler();
            _Merger = new MergeMeshHandler();
            _Apply = new ApplyMeshHandler();
            _Normals = new NormalsHandler();

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
            if (!IsFree) return;

            IsFree = false;
            _Entity = entity;
            chunkVoxels.MeshingInProgress = true;

            var currentHandle = JobHandle.CombineDependencies(dependency, chunkVoxels.AsyncReadJobHandle, chunkVoxels.AsyncWriteJobHandle);

            _Normals.Schedule(ref chunkVoxels, currentHandle);
            _Core.Schedule(ref chunkVoxels, ref _Normals, _Normals.JobHandle);
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

            if (!mgr.Exists(_Entity)) return true;

            outEntity = _Entity;

            if (mgr.HasComponent<TerrainChunkVoxels>(_Entity))
            {
                var voxels = mgr.GetComponentData<TerrainChunkVoxels>(_Entity);
                voxels.MeshingInProgress = false;
                mgr.SetComponentData(_Entity, voxels);
            }

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