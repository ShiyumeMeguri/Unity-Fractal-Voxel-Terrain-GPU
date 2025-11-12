using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    internal struct NormalsHandler : ISubHandler
    {
        public NativeArray<float3> VoxelNormals;
        public JobHandle JobHandle;
        private int3 _paddedChunkSize;

        public void Init(TerrainConfig config)
        {
            _paddedChunkSize = config.PaddedChunkSize;
            int volume = _paddedChunkSize.x * _paddedChunkSize.y * _paddedChunkSize.z;
            VoxelNormals = new NativeArray<float3>(volume, Allocator.Persistent);
        }

        public void Schedule(ref TerrainChunkVoxels voxels, JobHandle dependency)
        {
            var job = new NormalsCalculateJob
            {
                Densities = voxels.Voxels,
                Normals = VoxelNormals,
                ChunkSize = _paddedChunkSize
            };
            JobHandle = job.Schedule(VoxelNormals.Length, 256, dependency);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            if (VoxelNormals.IsCreated) VoxelNormals.Dispose();
        }
    }
}