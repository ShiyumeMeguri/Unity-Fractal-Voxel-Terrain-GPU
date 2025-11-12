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
            if (!IsFree) return;

            IsFree = false;
            _Entity = entity;

            chunkVoxels.MeshingInProgress = true;

            // 组合所有传入的依赖项
            var deps = new NativeArray<JobHandle>(4, Allocator.Temp)
            {
                [0] = dependency,
                [1] = _JobHandle, // 上一帧可能留下的句柄
                [2] = chunkVoxels.AsyncReadJobHandle,
                [3] = chunkVoxels.AsyncWriteJobHandle
            };
            JobHandle currentHandle = JobHandle.CombineDependencies(deps);
            deps.Dispose();

            // 1. 计算法线
            _Normals.Schedule(ref chunkVoxels, currentHandle);
            currentHandle = _Normals.JobHandle;

            // 2. 生成核心网格
            _Core.Schedule(ref chunkVoxels, ref _Normals, currentHandle);

            // 3. 生成裙边网格 (依赖核心网格和法线)
            _Skirt.Schedule(ref chunkVoxels, ref _Core, ref _Normals, _Core.JobHandle);

            // 4. 合并核心网格和裙边网格
            _Merger.Schedule(ref _Core, ref _Skirt);

            // 5. 准备应用到Unity Mesh
            _Apply.Schedule(ref _Merger);

            // 最终的句柄是Apply步骤的句柄
            _JobHandle = _Apply.JobHandle;

            // 更新体素数据的读取依赖
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

            var voxels = mgr.GetComponentData<TerrainChunkVoxels>(_Entity);
            voxels.MeshingInProgress = false;
            mgr.SetComponentData(_Entity, voxels);

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