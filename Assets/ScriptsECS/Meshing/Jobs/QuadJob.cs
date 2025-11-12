// Assets/ScriptsECS/Meshing/Jobs/QuadJob.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct QuadJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelData> Voxels;
        [ReadOnly] public NativeArray<int> VertexIndices;
        [ReadOnly] public NativeArray<byte> Enabled;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> Triangles;
        public NativeCounter.Concurrent TriangleCounter;
        [ReadOnly] public int3 ChunkSize;

        private static readonly int[] shifts = { 0, 3, 8 };
        private const int EMPTY_MASK = 1 << 0 | 1 << 3 | 1 << 8;

        public void Execute(int index)
        {
            var pos = VoxelUtils.To3DIndex(index, ChunkSize);
            if (math.any(pos >= ChunkSize - 2)) return;

            byte code = Enabled[index];
            if (code == 0 || code == 255) return;

            ushort enabledEdges = EdgeMaskUtils.EDGE_MASKS[code];
            if ((enabledEdges & EMPTY_MASK) == 0) return;

            for (int i = 0; i < 3; i++)
            {
                if (math.any(pos < (1 - VoxelUtils.DC_AXES[i]))) continue;
                if (((enabledEdges >> shifts[i]) & 1) == 1)
                {
                    CheckEdge(index, i, pos);
                }
            }
        }

        private void CheckEdge(int index, int direction, int3 pos)
        {
            int endIndex = index + VoxelUtils.To1DIndex((uint3)VoxelUtils.DC_AXES[direction], ChunkSize);

            int4 indices = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var cornerPos = pos + VoxelUtils.DC_ADJACENT[direction, i];
                indices[i] = VertexIndices[VoxelUtils.To1DIndex((uint3)cornerPos, ChunkSize)];
            }

            if (math.cmax(indices) == int.MaxValue) return;

            int triIndex = TriangleCounter.Add(2) * 3;
            bool flip = Voxels[endIndex].Density >= 0.0;

            Triangles[triIndex + (flip ? 0 : 2)] = indices[0];
            Triangles[triIndex + 1] = indices[1];
            Triangles[triIndex + (flip ? 2 : 0)] = indices[2];

            Triangles[triIndex + 3] = indices[0];
            Triangles[triIndex + 4] = indices[2];
            Triangles[triIndex + 5] = indices[3];
        }
    }
}