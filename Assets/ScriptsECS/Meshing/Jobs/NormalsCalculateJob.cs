using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ruri.Voxel
{
    [BurstCompile]
    public struct NormalsCalculateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelData> Densities;
        [WriteOnly] public NativeArray<float3> Normals;
        [ReadOnly] public int3 ChunkSize;

        public void Execute(int index)
        {
            var pos = VoxelUtils.To3DIndex(index, ChunkSize);
            if (math.any(pos == 0) || math.any(pos >= ChunkSize - 1))
            {
                Normals[index] = new float3(0, 1, 0);
                return;
            }

            float dx = Densities[index + VoxelUtils.To1DIndex(new int3(1, 0, 0), ChunkSize)].Density - Densities[index - VoxelUtils.To1DIndex(new int3(1, 0, 0), ChunkSize)].Density;
            float dy = Densities[index + VoxelUtils.To1DIndex(new int3(0, 1, 0), ChunkSize)].Density - Densities[index - VoxelUtils.To1DIndex(new int3(0, 1, 0), ChunkSize)].Density;
            float dz = Densities[index + VoxelUtils.To1DIndex(new int3(0, 0, 1), ChunkSize)].Density - Densities[index - VoxelUtils.To1DIndex(new int3(0, 0, 1), ChunkSize)].Density;

            Normals[index] = math.normalizesafe(new float3(-dx, -dy, -dz));
        }
    }
}