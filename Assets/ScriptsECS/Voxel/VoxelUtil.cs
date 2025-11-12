using Unity.Mathematics;
using UnityEngine;

namespace OptIn.Voxel
{
    public static class VoxelUtil
    {
        public static int3 To3DIndex(int index, int3 chunkSize)
        {
            return new int3 { z = index % chunkSize.z, y = (index / chunkSize.z) % chunkSize.y, x = index / (chunkSize.y * chunkSize.z) };
        }

        public static int To1DIndex(int3 index, int3 chunkSize)
        {
            return index.z + index.y * chunkSize.z + index.x * chunkSize.y * chunkSize.z;
        }

        public static int To1DIndex(Vector3Int index, Vector3Int chunkSize)
        {
            return To1DIndex(new int3(index.x, index.y, index.z), new int3(chunkSize.x, chunkSize.y, chunkSize.z));
        }

        public static Vector3Int WorldToChunk(Vector3 worldPosition, Vector3Int chunkSize)
        {
            return new Vector3Int
            {
                x = Floor(worldPosition.x / chunkSize.x),
                y = Floor(worldPosition.y / chunkSize.y),
                z = Floor(worldPosition.z / chunkSize.z)
            };
        }

        public static Vector3 ChunkToWorld(Vector3Int chunkPosition, Vector3Int chunkSize)
        {
            return chunkPosition * chunkSize;
        }

        public static Vector3Int WorldToGrid(Vector3 worldPosition, Vector3 chunkWorldPosition, Vector3Int chunkSize)
        {
            Vector3 localPos = worldPosition - chunkWorldPosition;
            return new Vector3Int(Floor(localPos.x), Floor(localPos.y), Floor(localPos.z));
        }

        public static bool BoundaryCheck(int3 position, int3 chunkSize)
        {
            return position.x >= 0 && position.x < chunkSize.x &&
                   position.y >= 0 && position.y < chunkSize.y &&
                   position.z >= 0 && position.z < chunkSize.z;
        }

        public static bool BoundaryCheck(Vector3Int position, Vector3Int chunkSize)
        {
            return position.x >= 0 && position.x < chunkSize.x &&
                   position.y >= 0 && position.y < chunkSize.y &&
                   position.z >= 0 && position.z < chunkSize.z;
        }

        public static Vector3Int ToVector3Int(int3 v) => new Vector3Int(v.x, v.y, v.z);
        public static int3 ToInt3(Vector3Int v) => new int3(v.x, v.y, v.z);

        public static int Floor(float x)
        {
            int xi = (int)x;
            return x < xi ? xi - 1 : xi;
        }

        // --- Constants ---

        public static readonly int[] DirectionAlignedX = { 2, 2, 0, 0, 0, 0 };
        public static readonly int[] DirectionAlignedY = { 1, 1, 2, 2, 1, 1 };
        public static readonly int[] DirectionAlignedZ = { 0, 0, 1, 1, 2, 2 };

        public static readonly int3[] VoxelDirectionOffsets =
        {
            new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0),
            new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1)
        };

        public static readonly float3[] CubeVertices =
        {
            new float3(0f, 0f, 0f), new float3(1f, 0f, 0f), new float3(1f, 0f, 1f), new float3(0f, 0f, 1f),
            new float3(0f, 1f, 0f), new float3(1f, 1f, 0f), new float3(1f, 1f, 1f), new float3(0f, 1f, 1f)
        };

        public static readonly int[] CubeFaces =
        {
            1, 2, 5, 6, 0, 3, 4, 7, 4, 5, 7, 6,
            0, 1, 3, 2, 3, 2, 7, 6, 0, 1, 4, 5
        };

        public static readonly float2[] CubeUVs =
        {
            new float2(0f, 0f), new float2(1.0f, 0f), new float2(0f, 1.0f), new float2(1.0f, 1.0f)
        };

        public static readonly int[] CubeIndices =
        {
            0, 3, 1, 0, 2, 3, // Right Face
            1, 3, 0, 3, 2, 0, // Left Face
            0, 3, 1, 0, 2, 3, // Top Face
            1, 3, 0, 3, 2, 0, // Bottom Face
            1, 3, 0, 3, 2, 0, // Front Face
            0, 3, 1, 0, 2, 3  // Back Face
        };

        public static readonly int3[] DC_VERT =
        {
            new int3(0, 0, 0), new int3(0, 0, 1), new int3(0, 1, 0), new int3(0, 1, 1),
            new int3(1, 0, 0), new int3(1, 0, 1), new int3(1, 1, 0), new int3(1, 1, 1)
        };

        public static readonly int[,] DC_EDGE =
        {
            {0, 4}, {1, 5}, {2, 6}, {3, 7}, {5, 7}, {1, 3}, {4, 6}, {0, 2},
            {4, 5}, {0, 1}, {6, 7}, {2, 3}
        };

        public static readonly int3[] DC_AXES = { new int3(1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1) };

        public static readonly int3[,] DC_ADJACENT =
        {
            { new int3(0, 0, 0), new int3(0, -1, 0), new int3(0, -1, -1), new int3(0, 0, -1) },
            { new int3(0, 0, 0), new int3(0, 0, -1), new int3(-1, 0, -1), new int3(-1, 0, 0) },
            { new int3(0, 0, 0), new int3(-1, 0, 0), new int3(-1, -1, 0), new int3(0, -1, 0) }
        };
    }
}