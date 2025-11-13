// Assets/ScriptsECS/Meshing/Jobs/CornerJob.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ruri.Voxel
{
    [BurstCompile]
    public struct CornerJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<uint> Bits;
        [WriteOnly] public NativeArray<byte> Enabled;
        [ReadOnly] public int3 ChunkSize;

        public void Execute(int index)
        {
            var pos = VoxelUtils.To3DIndex(index, ChunkSize);
            if (math.any(pos >= ChunkSize - 1))
            {
                Enabled[index] = 0;
                return;
            }

            int code = 0;
            for (int i = 0; i < 8; i++)
            {
                int cornerIndex = VoxelUtils.To1DIndex((uint3)(pos + VoxelUtils.DC_VERT[i]), ChunkSize);
                if (cornerIndex >= Bits.Length * 32) continue;

                int component = cornerIndex >> 5;
                int shift = cornerIndex & 31;
                if ((Bits[component] & (1u << shift)) == 0)
                {
                    code |= (1 << i);
                }
            }
            Enabled[index] = (byte)code;
        }
    }
}