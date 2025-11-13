// Meshing/Jobs/QuadJob.cs
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

            // [修复] 正确获取四边形的四个顶点索引
            int3 offset = pos + VoxelUtils.DC_AXES[direction] - 1;
            int4 indices;
            indices.x = VertexIndices[VoxelUtils.To1DIndex((uint3)(offset + (int3)DirectionOffsetUtils.PERPENDICULAR_OFFSETS[direction * 4 + 0]), ChunkSize)];
            indices.y = VertexIndices[VoxelUtils.To1DIndex((uint3)(offset + (int3)DirectionOffsetUtils.PERPENDICULAR_OFFSETS[direction * 4 + 1]), ChunkSize)];
            indices.z = VertexIndices[VoxelUtils.To1DIndex((uint3)(offset + (int3)DirectionOffsetUtils.PERPENDICULAR_OFFSETS[direction * 4 + 2]), ChunkSize)];
            indices.w = VertexIndices[VoxelUtils.To1DIndex((uint3)(offset + (int3)DirectionOffsetUtils.PERPENDICULAR_OFFSETS[direction * 4 + 3]), ChunkSize)];

            // [修复] 采用更明确的检查方式
            if (indices.x == int.MaxValue || indices.y == int.MaxValue || indices.z == int.MaxValue || indices.w == int.MaxValue) return;

            int triIndex = TriangleCounter.Add(2) * 3;
            bool flip = Voxels[endIndex].Density < 0.0f;

            // [修复] 调整顶点顺序以匹配参考代码的三角形生成方式
            Triangles[triIndex + (flip ? 0 : 2)] = indices.x;
            Triangles[triIndex + 1] = indices.y;
            Triangles[triIndex + (flip ? 2 : 0)] = indices.z;

            Triangles[triIndex + 3] = indices.z;
            Triangles[triIndex + 4] = indices.w;
            Triangles[triIndex + 5] = indices.x;
        }
    }
}