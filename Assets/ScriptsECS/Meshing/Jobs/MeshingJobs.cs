using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct BoundsJob : IJob
    {
        [ReadOnly] public NativeArray<float3> MergedVerticesPositions;
        [ReadOnly] public NativeReference<int> TotalVertexCount;
        public NativeReference<MinMaxAABB> Bounds;

        public void Execute()
        {
            if (TotalVertexCount.Value == 0)
            {
                Bounds.Value = new MinMaxAABB { Min = float3.zero, Max = float3.zero };
                return;
            }

            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);

            for (int i = 0; i < TotalVertexCount.Value; i++)
            {
                min = math.min(min, MergedVerticesPositions[i]);
                max = math.max(max, MergedVerticesPositions[i]);
            }
            Bounds.Value = new MinMaxAABB { Min = min, Max = max };
        }
    }

    [BurstCompile]
    public struct SetMeshDataJob : IJob
    {
        [WriteOnly] public Mesh.MeshData Data;
        [ReadOnly] public Vertices MergedVertices;
        [ReadOnly] public NativeArray<int> MergedIndices;
        [ReadOnly] public NativeArray<int> SubmeshIndexOffsets;
        [ReadOnly] public NativeArray<int> SubmeshIndexCounts;
        [ReadOnly] public NativeReference<int> TotalVertexCount;
        [ReadOnly] public NativeReference<int> TotalIndexCount;

        public void Execute()
        {
            int vertexCount = TotalVertexCount.Value;
            int indexCount = TotalIndexCount.Value;

            if (vertexCount == 0 || indexCount == 0) return;

            MergedVertices.SetMeshDataAttributes(vertexCount, Data);
            Data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
            MergedIndices.GetSubArray(0, indexCount).CopyTo(Data.GetIndexData<int>());

            Data.subMeshCount = 7;
            for (int i = 0; i < 7; i++)
            {
                Data.SetSubMesh(i, new SubMeshDescriptor
                {
                    indexStart = SubmeshIndexOffsets[i],
                    indexCount = SubmeshIndexCounts[i],
                    topology = MeshTopology.Triangles,
                }, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            }
        }
    }

    [BurstCompile]
    public struct MergeMeshJob : IJob
    {
        [ReadOnly] public Vertices Vertices;
        [ReadOnly] public NativeArray<int> Indices;
        [ReadOnly] public NativeCounter VertexCounter;
        [ReadOnly] public NativeCounter TriangleCounter;

        [ReadOnly] public Vertices SkirtVertices;
        [ReadOnly] public NativeCounter SkirtVertexCounter;

        [ReadOnly] public NativeArray<int> SkirtStitchedIndices;
        [ReadOnly] public NativeCounter SkirtStitchedTriangleCounter;
        [ReadOnly] public NativeArray<int> SkirtForcedPerFaceIndices;
        [ReadOnly] public NativeMultiCounter SkirtForcedTriangleCounter;

        public NativeArray<int> SubmeshIndexOffsets;
        public NativeArray<int> SubmeshIndexCounts;
        public NativeReference<int> TotalVertexCount;
        public NativeReference<int> TotalIndexCount;

        public Vertices MergedVertices;
        public NativeArray<int> MergedIndices;

        private static void CopyVertices(Vertices src, Vertices dst, int dstOffset, int length)
        {
            if (length == 0) return;
            src.positions.GetSubArray(0, length).CopyTo(dst.positions.GetSubArray(dstOffset, length));
            src.normals.GetSubArray(0, length).CopyTo(dst.normals.GetSubArray(dstOffset, length));
            src.layers.GetSubArray(0, length).CopyTo(dst.layers.GetSubArray(dstOffset, length));
            src.colours.GetSubArray(0, length).CopyTo(dst.colours.GetSubArray(dstOffset, length));
        }

        private static void CopyIndices<T>(NativeArray<T> src, NativeArray<T> dst, int dstOffset, int length) where T : unmanaged
        {
            if (length == 0) return;
            src.GetSubArray(0, length).CopyTo(dst.GetSubArray(dstOffset, length));
        }

        public void Execute()
        {
            int mainVertexCount = VertexCounter.Count;
            int skirtVertexCount = SkirtVertexCounter.Count;
            TotalVertexCount.Value = mainVertexCount + skirtVertexCount;

            CopyVertices(Vertices, MergedVertices, 0, mainVertexCount);
            CopyVertices(SkirtVertices, MergedVertices, mainVertexCount, skirtVertexCount);

            int mainIndexCount = TriangleCounter.Count * 3;
            int skirtStitchedIndexCount = SkirtStitchedTriangleCounter.Count * 3;

            CopyIndices(Indices, MergedIndices, 0, mainIndexCount);
            CopyIndices(SkirtStitchedIndices, MergedIndices, mainIndexCount, skirtStitchedIndexCount);

            SubmeshIndexOffsets[0] = 0;
            SubmeshIndexCounts[0] = mainIndexCount + skirtStitchedIndexCount;

            int currentIndexOffset = mainIndexCount + skirtStitchedIndexCount;
            int totalIndexCount = currentIndexOffset;

            var forcedCounts = SkirtForcedTriangleCounter.ToArray();
            for (int face = 0; face < 6; face++)
            {
                int perFaceIndexCount = forcedCounts[face] * 3;
                int perFaceMaxIndices = VoxelUtils.SKIRT_FACE * 6;

                if (perFaceIndexCount > 0)
                {
                    CopyIndices(SkirtForcedPerFaceIndices.GetSubArray(face * perFaceMaxIndices, perFaceIndexCount), MergedIndices, currentIndexOffset, perFaceIndexCount);
                }

                SubmeshIndexOffsets[face + 1] = currentIndexOffset;
                SubmeshIndexCounts[face + 1] = perFaceIndexCount;

                currentIndexOffset += perFaceIndexCount;
                totalIndexCount += perFaceIndexCount;
            }

            TotalIndexCount.Value = totalIndexCount;
        }
    }
}