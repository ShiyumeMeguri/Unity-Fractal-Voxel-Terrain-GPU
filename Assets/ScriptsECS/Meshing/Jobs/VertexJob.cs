// Assets/ScriptsECS/Meshing/Jobs/VertexJob.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct VertexJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelData> Voxels;
        [ReadOnly] public NativeArray<float3> VoxelNormals;
        [ReadOnly] public NativeArray<byte> Enabled;
        [WriteOnly] public NativeArray<int> Indices;
        [NativeDisableParallelForRestriction] public Vertices Vertices;
        public NativeCounter.Concurrent VertexCounter;
        [ReadOnly] public int3 ChunkSize;

        public void Execute(int index)
        {
            Indices[index] = int.MaxValue;
            uint enabledCorners = Enabled[index];
            if (enabledCorners == 0 || enabledCorners == 255) return;

            var pos = VoxelUtils.To3DIndex(index, ChunkSize);
            if (math.any(pos >= ChunkSize - 2)) return;

            ushort code = EdgeMaskUtils.EDGE_MASKS[enabledCorners];
            int count = math.countbits((int)code);
            if (count == 0) return;

            var vertex = new Vertices.Single();
            for (int edge = 0; edge < 12; edge++)
            {
                if (((code >> edge) & 1) == 0) continue;

                uint3 startOffset = (uint3)VoxelUtils.DC_VERT[VoxelUtils.DC_EDGE[edge, 0]];
                uint3 endOffset = (uint3)VoxelUtils.DC_VERT[VoxelUtils.DC_EDGE[edge, 1]];
                int startIndex = VoxelUtils.To1DIndex((uint3)(pos + (int3)startOffset), ChunkSize);
                int endIndex = VoxelUtils.To1DIndex((uint3)(pos + (int3)endOffset), ChunkSize);

                vertex.Add((float3)startOffset, (float3)endOffset, startIndex, endIndex, ref Voxels);
            }

            vertex.Finalize(count);
            vertex.position += pos;

            int vertexIndex = VertexCounter.Increment();
            Indices[index] = vertexIndex;
            Vertices[vertexIndex] = vertex;
        }
    }
}