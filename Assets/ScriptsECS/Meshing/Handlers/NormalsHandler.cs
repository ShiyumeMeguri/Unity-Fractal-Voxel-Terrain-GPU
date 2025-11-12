// Assets/ScriptsECS/Meshing/Handlers/NormalsHandler.cs
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static OptIn.Voxel.VoxelUtils;

namespace OptIn.Voxel.Meshing
{
    internal struct NormalsHandler : ISubHandler
    {
        public NativeArray<float3> VoxelNormals;
        public JobHandle JobHandle;

        public void Init(TerrainConfig config)
        {
            VoxelNormals = new NativeArray<float3>(VOLUME, Allocator.Persistent);
        }

        public void Schedule(ref TerrainChunkVoxels voxels, JobHandle dependency)
        {
            // 在这里调度一个计算法线的Job，例如 NormalsCalculateJob
            // 为了简化，我们假设您有一个可以计算法线的Job
            // 这里我们仅为结构占位，并传递依赖项
            // 实际的法线计算Job需要您根据VoxelData来实现
            JobHandle = dependency; 
        }

        public void Dispose()
        {
            JobHandle.Complete();
            if (VoxelNormals.IsCreated) VoxelNormals.Dispose();
        }
    }
}