using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    internal struct CoreMeshHandler : ISubHandler
    {
        public VoxelMeshBuilder.NativeMeshData MeshData;
        public JobHandle JobHandle;

        public void Init(TerrainConfig config)
        {
            MeshData = new VoxelMeshBuilder.NativeMeshData(config.PaddedChunkSize);
        }

        public void Schedule(ref ChunkVoxelData chunkVoxels, JobHandle dependency)
        {
            JobHandle = VoxelMeshBuilder.ScheduleMeshingJob(chunkVoxels.Voxels, VoxelUtil.PaddedChunkSize, MeshData, dependency);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            MeshData.Dispose();
        }
    }
}