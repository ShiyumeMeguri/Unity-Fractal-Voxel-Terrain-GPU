using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct SkirtQuadJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelData> Voxels;
        [ReadOnly] public NativeArray<int> SkirtVertexIndicesGenerated;
        [ReadOnly] public NativeArray<int> SkirtVertexIndicesCopied;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> SkirtForcedPerFaceIndices;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> SkirtStitchedIndices;

        public NativeCounter.Concurrent SkirtStitchedTriangleCounter;
        public NativeMultiCounter.Concurrent SkirtForcedTriangleCounter;
        [ReadOnly] public int3 PaddedChunkSize;

        private int FetchIndex(int3 position, int face)
        {
            int direction = face % 3;
            int2 flattened = VoxelUtils.FlattenToFaceRelative(position, direction);
            int other = position[direction];

            if (other < 0 || other > PaddedChunkSize.x - 3)
            {
                flattened += 1;
                flattened = math.clamp(flattened, 0, VoxelUtils.SKIRT_SIZE);
                int lookup = VoxelUtils.PosToIndex2D((uint2)flattened, VoxelUtils.SKIRT_SIZE);
                return SkirtVertexIndicesGenerated[lookup + VoxelUtils.SKIRT_FACE * face];
            }
            else
            {
                flattened = math.clamp(flattened, 0, PaddedChunkSize.x - 1);
                int lookup = VoxelUtils.PosToIndex2D((uint2)flattened, PaddedChunkSize.x);
                return SkirtVertexIndicesCopied[lookup + VoxelUtils.FACE * face];
            }
        }

        private void CheckEdge(uint2 flattened, uint3 unflattened, int edgeDirection, bool negative, bool force, int face)
        {
            uint3 forward = DirectionOffsetUtils.FORWARD_DIRECTION[edgeDirection];
            bool flip = !negative;

            if (!force)
            {
                int baseIndex = VoxelUtils.To1DIndex(unflattened, PaddedChunkSize);
                int endIndex = VoxelUtils.To1DIndex(unflattened + forward, PaddedChunkSize);

                if (Voxels[baseIndex].IsSolid == Voxels[endIndex].IsSolid) return;
                flip = Voxels[endIndex].IsSolid;
            }

            int3 offset = (int3)unflattened + (int3)forward - 1;
            if (force)
            {
                offset[edgeDirection] += negative ? -1 : 1;
            }

            int4 v = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                v[i] = FetchIndex(offset + (int3)DirectionOffsetUtils.PERPENDICULAR_OFFSETS[edgeDirection * 4 + i], face);
            }

            if (TryCalculateQuadOrTris(flip, v, out var data))
            {
                if (force)
                {
                    var faceIndicesSubArray = SkirtForcedPerFaceIndices.GetSubArray(face * VoxelUtils.SKIRT_FACE * 6, VoxelUtils.SKIRT_FACE * 6);
                    AddQuadsOrTris(data, SkirtForcedTriangleCounter.ToConcurrentNativeCounter(face), faceIndicesSubArray);
                }
                else
                {
                    AddQuadsOrTris(data, SkirtStitchedTriangleCounter, SkirtStitchedIndices);
                }
            }
        }

        private static readonly int3[] DEDUPE_TRIS_THING = {
            new int3(0, 2, 3), new int3(0, 1, 3), new int3(0, 1, 2),
            new int3(0, 1, 3), new int3(0, 1, 2), new int3(0, 1, 2)
        };
        private static readonly int3[] IGNORE_SPECIFIC_VALUE_TRI = { new int3(1, 2, 3), new int3(0, 2, 3), new int3(0, 1, 3), new int3(0, 1, 2) };

        private struct Triangulate { public int4 indices; public bool triangle; }

        private static void AddQuadsOrTris(Triangulate data, NativeCounter.Concurrent counter, NativeArray<int> indices)
        {
            int4 v = data.indices;
            if (data.triangle)
            {
                int triIndex = counter.Add(1) * 3;
                if (triIndex + 2 < indices.Length)
                {
                    indices[triIndex] = v.x;
                    indices[triIndex + 1] = v.y;
                    indices[triIndex + 2] = v.z;
                }
            }
            else
            {
                int triIndex = counter.Add(2) * 3;
                if (triIndex + 5 < indices.Length)
                {
                    indices[triIndex] = v.x; indices[triIndex + 1] = v.y; indices[triIndex + 2] = v.z;
                    indices[triIndex + 3] = v.z; indices[triIndex + 4] = v.w; indices[triIndex + 5] = v.x;
                }
            }
        }

        private static bool TryCalculateQuadOrTris(bool flip, int4 v, out Triangulate data)
        {
            data = default;
            int dupeType = 0;
            if (v.x == v.y) dupeType |= 1; if (v.x == v.z) dupeType |= 2; if (v.x == v.w) dupeType |= 4;
            if (v.y == v.z && v.x != v.y) dupeType |= 8; if (v.y == v.w && v.x != v.y && v.z != v.y) dupeType |= 16;
            if (v.z == v.w && v.x != v.z && v.y != v.z) dupeType |= 32;

            int bitmask = math.bitmask(v == int.MaxValue);
            if (math.countbits(dupeType) > 1 || math.countbits(bitmask) > 1) return false;

            if (math.countbits(bitmask) == 1)
            {
                int3 remapper = IGNORE_SPECIFIC_VALUE_TRI[math.tzcnt(bitmask)];
                int3 uniques = new int3(v[remapper.x], v[remapper.y], v[remapper.z]);
                data.triangle = true;
                data.indices.x = uniques[flip ? 0 : 2];
                data.indices.y = uniques[1];
                data.indices.z = uniques[flip ? 2 : 0];
                return true;
            }
            else if (dupeType == 0)
            {
                if (math.cmax(v) == int.MaxValue || math.cmin(v) < 0) return false;
                data.triangle = false;
                data.indices.x = v[flip ? 0 : 2];
                data.indices.y = v[1];
                data.indices.z = v[flip ? 2 : 0];
                data.indices.w = v[3];
                return true;
            }
            else
            {
                int3 remapper = DEDUPE_TRIS_THING[math.tzcnt(dupeType)];
                int3 uniques = new int3(v[remapper.x], v[remapper.y], v[remapper.z]);
                if (math.cmax(uniques) == int.MaxValue || math.cmin(v) < 0) return false;

                data.triangle = true;
                data.indices.x = uniques[flip ? 0 : 2];
                data.indices.y = uniques[1];
                data.indices.z = uniques[flip ? 2 : 0];
                return true;
            }
        }

        public void Execute(int index)
        {
            int face = index / VoxelUtils.FACE;
            int direction = face % 3;
            bool negative = face < 3;
            int localIndex = index % VoxelUtils.FACE;

            uint missing = negative ? 0u : (uint)PaddedChunkSize.x - 2;
            uint2 flattened = VoxelUtils.IndexToPos2D(localIndex, PaddedChunkSize.x);
            uint3 position = VoxelUtils.UnflattenFromFaceRelative(flattened, direction, missing);

            if (math.any(flattened >= (uint)(VoxelUtils.SKIRT_SIZE - 1))) return;

            for (int i = 0; i < 3; i++)
            {
                bool force = direction == i;
                if (position[i] >= PaddedChunkSize.x - 3 && !force) continue;
                CheckEdge(flattened, position, i, negative, force, face);
            }
        }
    }
}