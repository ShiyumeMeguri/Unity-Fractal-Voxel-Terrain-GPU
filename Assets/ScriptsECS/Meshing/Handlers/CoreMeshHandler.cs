using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    internal struct CoreMeshHandler : ISubHandler
    {
        public VoxelMeshBuilder.NativeMeshData MeshData;
        public JobHandle JobHandle;
        private int3 m_PaddedChunkSize; // 新增字段来存储尺寸

        public void Init(TerrainConfig config)
        {
            m_PaddedChunkSize = config.PaddedChunkSize; // 在初始化时存储
            MeshData = new VoxelMeshBuilder.NativeMeshData(config.PaddedChunkSize);
        }

        public void Schedule(ref ChunkVoxelData chunkVoxels, JobHandle dependency)
        {
            // 使用存储的尺寸而不是静态变量
            JobHandle = VoxelMeshBuilder.ScheduleMeshingJob(chunkVoxels.Voxels, m_PaddedChunkSize, MeshData, dependency);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            MeshData.Dispose();
        }
    }
}