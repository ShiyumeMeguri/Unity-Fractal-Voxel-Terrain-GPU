// Assets/ScriptsECS/Meshing/Jobs/CheckJob.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct CheckJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelData> Densities;
        [WriteOnly] public NativeArray<uint> Bits;

        public void Execute(int index)
        {
            uint packed = 0;
            int baseIndex = index * 32;
            int count = math.min(Densities.Length - baseIndex, 32);
            for (int j = 0; j < count; j++)
            {
                if (Densities[j + baseIndex].Density >= 0f)
                {
                    packed |= 1u << j;
                }
            }
            Bits[index] = packed;
        }
    }
}