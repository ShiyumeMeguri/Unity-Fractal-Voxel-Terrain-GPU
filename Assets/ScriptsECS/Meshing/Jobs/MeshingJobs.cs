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
        [ReadOnly] public NativeArray<GPUVertex> Vertices;
        [ReadOnly] public NativeArray<int> Indices;
        [ReadOnly] public NativeCounter VertexCounter;
        [ReadOnly] public NativeCounter TriangleCounter;

        [ReadOnly] public Vertices SkirtVertices;
        [ReadOnly] public NativeArray<int> SkirtStitchedIndices;
        [ReadOnly] public NativeArray<int> SkirtForcedPerFaceIndices;
        [ReadOnly] public NativeCounter SkirtVertexCounter;
        [ReadOnly] public NativeCounter SkirtStitchedTriangleCounter;
        [ReadOnly] public NativeMultiCounter SkirtForcedTriangleCounter;
        
        public NativeArray<int> SubmeshIndexOffsets;
        public NativeArray<int> SubmeshIndexCounts;
        public NativeReference<int> TotalVertexCount;
        public NativeReference<int> TotalIndexCount;

        public Vertices MergedVertices;
        public NativeArray<int> MergedIndices;

        public void Execute()
        {
            // ... (完整的合并逻辑)
        }
    }
}