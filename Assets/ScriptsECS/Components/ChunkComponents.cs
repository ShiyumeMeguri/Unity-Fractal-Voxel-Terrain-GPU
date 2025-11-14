using TreeEditor;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ruri.Voxel
{
    /// <summary>
    /// 代表一个地形区块的实体组件。
    /// 包含了其在八叉树中的节点信息。
    /// </summary>
    public struct TerrainChunk : IComponentData
    {
        public OctreeNode node;
        // 精简：移除了skirts和skirtMask
        public BitField32 neighbourMask;
    }

    /// <summary>
    /// 存储单个区块的体素数据。这是一个SoA（结构数组）实现，以优化内存和性能。
    /// </summary>
    public struct TerrainChunkVoxels : IComponentData, IEnableableComponent
    {
        public NativeArray<VoxelData> data;
        public JobHandle asyncWriteJobHandle;
        public JobHandle asyncReadJobHandle;
        public bool meshingInProgress;
    }

    /// <summary>
    /// 存储为区块生成的网格数据。
    /// </summary>
    public struct TerrainChunkMesh : IComponentData, IEnableableComponent
    {
        public NativeArray<float3> vertices;
        public NativeArray<float3> normals;
        public NativeArray<int> mainMeshIndices;
        public JobHandle accessJobHandle;

        public void Dispose()
        {
            accessJobHandle.Complete();
            if (vertices.IsCreated) vertices.Dispose();
            if (normals.IsCreated) normals.Dispose();
            if (mainMeshIndices.IsCreated) mainMeshIndices.Dispose();
        }
    }
}