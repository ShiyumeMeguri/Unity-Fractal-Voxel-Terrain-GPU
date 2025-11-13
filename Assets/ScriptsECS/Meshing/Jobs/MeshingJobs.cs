using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ruri.Voxel
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

        // REFACTOR: All skirt-related fields are removed.

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
            src.uvs.GetSubArray(0, length).CopyTo(dst.uvs.GetSubArray(dstOffset, length));
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
            TotalVertexCount.Value = mainVertexCount;

            CopyVertices(Vertices, MergedVertices, 0, mainVertexCount);

            int mainIndexCount = TriangleCounter.Count * 3;
            TotalIndexCount.Value = mainIndexCount;

            CopyIndices(Indices, MergedIndices, 0, mainIndexCount);

            // Main mesh is submesh 0
            SubmeshIndexOffsets[0] = 0;
            SubmeshIndexCounts[0] = mainIndexCount;

            // Other submeshes (for skirts) are now empty.
            for (int i = 1; i < 7; i++)
            {
                SubmeshIndexOffsets[i] = 0;
                SubmeshIndexCounts[i] = 0;
            }
        }
    }
}