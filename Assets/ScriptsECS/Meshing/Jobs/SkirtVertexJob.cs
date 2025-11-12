using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile(CompileSynchronously = true)]
    public struct SkirtVertexJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelData> Voxels;
        [ReadOnly] public NativeArray<float3> voxelNormals;
        [ReadOnly] public NativeArray<bool> WithinThreshold;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> SkirtVertexIndicesGenerated;
        [WriteOnly, NativeDisableParallelForRestriction] public Vertices SkirtVertices;
        public NativeCounter.Concurrent SkirtVertexCounter;
        [ReadOnly] public NativeCounter VertexCounter;
        [ReadOnly] public int3 PaddedChunkSize;

        public static readonly uint2[] EDGE_POSITIONS_0_CUSTOM = { new uint2(0, 0), new uint2(0, 1), new uint2(1, 1), new uint2(1, 0) };
        public static readonly uint2[] EDGE_POSITIONS_1_CUSTOM = { new uint2(0, 1), new uint2(1, 1), new uint2(1, 0), new uint2(0, 0) };

        private struct VertexToSpawn
        {
            public Vertices.Single inner;
            public bool shouldSpawn;
            public bool useWorldPosition;
            public float3 worldPosition;
        }

        public void Execute(int index)
        {
            int face = index / VoxelUtils.SKIRT_FACE;
            int direction = face % 3;
            bool negative = face < 3;
            uint missing = negative ? 0u : (uint)PaddedChunkSize.x - 3;
            int localIndex = index % VoxelUtils.SKIRT_FACE;

            SkirtVertexIndicesGenerated[index] = int.MaxValue;

            uint2 flatten = VoxelUtils.IndexToPos2D(localIndex, VoxelUtils.SKIRT_SIZE);
            uint3 unoffsetted = SkirtUtils.UnflattenFromFaceRelative(flatten, direction, missing);

            bool4 minMaxMask = new bool4(flatten.x == 0, flatten.y == 0, flatten.x == (VoxelUtils.SKIRT_SIZE - 1), flatten.y == (VoxelUtils.SKIRT_SIZE - 1));
            int count = BitUtils.CountTrue(minMaxMask);

            VertexToSpawn vertex;
            if (count == 2)
            {
                vertex = CreateCorner(direction, negative, flatten);
            }
            else if (count == 1)
            {
                vertex = CreateSurfaceNets1D(flatten, minMaxMask, negative, face);
            }
            else
            {
                uint3 position = SkirtUtils.UnflattenFromFaceRelative(flatten - 1, direction, missing);
                vertex = CreateSurfaceNets2D(face, negative, position);
            }

            if (vertex.shouldSpawn)
            {
                int vertexIndex = SkirtVertexCounter.Increment();
                vertex.inner.position += vertex.useWorldPosition ? vertex.worldPosition : (float3)unoffsetted;
                SkirtVertices[vertexIndex] = vertex.inner;
                SkirtVertexIndicesGenerated[index] = VertexCounter.Count + vertexIndex;
            }
        }

        private VertexToSpawn CreateCorner(int direction, bool negative, uint2 flatten)
        {
            return new VertexToSpawn { shouldSpawn = false };
        }

        private VertexToSpawn CreateSurfaceNets2D(int face, bool negative, uint3 position)
        {
            int faceDir = face % 3;
            uint2 flat = (uint2)VoxelUtils.FlattenToFaceRelative((int3)position, faceDir);

            Vertices.Single vertex = new Vertices.Single();
            Vertices.Single forcedVertex = new Vertices.Single();
            bool force = false;
            int count = 0;

            for (int edge = 0; edge < 4; edge++)
            {
                uint2 startOffset2D = EDGE_POSITIONS_0_CUSTOM[edge];
                uint2 endOffset2D = EDGE_POSITIONS_1_CUSTOM[edge];

                uint3 startOffset = VoxelUtils.UnflattenFromFaceRelative(startOffset2D, faceDir, negative ? 0u : 1u);
                uint3 endOffset = VoxelUtils.UnflattenFromFaceRelative(endOffset2D, faceDir, negative ? 0u : 1u);

                int startIndex = VoxelUtils.To1DIndex(startOffset + position, PaddedChunkSize);
                int endIndex = VoxelUtils.To1DIndex(endOffset + position, PaddedChunkSize);

                int withinThresholdIndex = VoxelUtils.PosToIndex2D(startOffset2D + flat, PaddedChunkSize.x) + face * VoxelUtils.FACE;
                force |= WithinThreshold[withinThresholdIndex];

                if (Voxels[startIndex].IsSolid != Voxels[endIndex].IsSolid)
                {
                    count++;
                    vertex.Add((float3)startOffset, (float3)endOffset, startIndex, endIndex, ref Voxels, ref voxelNormals);
                }
                forcedVertex.AddLerped((float3)startOffset, (float3)endOffset, startIndex, endIndex, 0.5f, ref Voxels, ref voxelNormals);
            }

            if (count == 0)
            {
                if (force)
                {
                    forcedVertex.Finalize(4);
                    forcedVertex.position += SkirtUtils.UnflattenFromFaceRelative(new float2(-0.5f), faceDir, negative ? 0f : 1f);
                    return new VertexToSpawn { inner = forcedVertex, shouldSpawn = true };
                }
                return default;
            }

            vertex.Finalize(count);
            vertex.position -= (float3)SkirtUtils.UnflattenFromFaceRelative(new uint2(1), faceDir);
            return new VertexToSpawn { inner = vertex, shouldSpawn = true };
        }

        private VertexToSpawn CreateSurfaceNets1D(uint2 flatten, bool4 minMaxMask, bool negative, int face)
        {
            int faceDir = face % 3;
            bool2 mask = new bool2(minMaxMask.x || minMaxMask.z, minMaxMask.y || minMaxMask.w);
            int edgeDir = SkirtUtils.GetEdgeDirFaceRelative(mask, faceDir);

            uint missing = negative ? 0u : (uint)PaddedChunkSize.x - 2;

            flatten = math.clamp(flatten, 0u, (uint)(PaddedChunkSize.x - 2));

            float3 worldPos = SkirtUtils.UnflattenFromFaceRelative((float2)flatten, faceDir, missing);

            Vertices.Single vertex = new Vertices.Single();
            Vertices.Single forcedVertex = new Vertices.Single();
            bool force = false;
            bool spawn = false;

            uint3 endOffset = DirectionOffsetUtils.FORWARD_DIRECTION[edgeDir];
            uint3 unoffsetted = SkirtUtils.UnflattenFromFaceRelative(flatten, faceDir, missing);

            int startIndex = VoxelUtils.To1DIndex(unoffsetted - endOffset, PaddedChunkSize);
            int endIndex = VoxelUtils.To1DIndex(unoffsetted, PaddedChunkSize);

            int2 startPos2D = VoxelUtils.FlattenToFaceRelative((int3)(unoffsetted - endOffset), faceDir);

            force |= WithinThreshold[VoxelUtils.PosToIndex2D((uint2)startPos2D, PaddedChunkSize.x) + face * VoxelUtils.FACE];
            force |= WithinThreshold[VoxelUtils.PosToIndex2D(flatten, PaddedChunkSize.x) + face * VoxelUtils.FACE];

            if (Voxels[startIndex].IsSolid != Voxels[endIndex].IsSolid)
            {
                spawn = true;
                vertex.Add(-(float3)endOffset, float3.zero, startIndex, endIndex, ref Voxels, ref voxelNormals);
            }
            forcedVertex.AddLerped(-(float3)endOffset, float3.zero, startIndex, endIndex, 0.5f, ref Voxels, ref voxelNormals);

            if (spawn)
            {
                vertex.Finalize(1);
                return new VertexToSpawn { inner = vertex, worldPosition = worldPos, shouldSpawn = true, useWorldPosition = true };
            }

            if (force)
            {
                forcedVertex.Finalize(1);
                forcedVertex.position += -(float3)endOffset * 0.5f;
                return new VertexToSpawn { worldPosition = worldPos, inner = forcedVertex, shouldSpawn = true, useWorldPosition = true };
            }

            return default;
        }
    }
}