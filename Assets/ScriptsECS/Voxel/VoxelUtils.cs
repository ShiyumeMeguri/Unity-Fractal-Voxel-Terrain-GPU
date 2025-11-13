// Utils/VoxelUtils.cs
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace Ruri.Voxel
{
    public static class VoxelUtils
    {
        // [修正] 将所有常量定义移到这里，并统一数据类型
        public const int PHYSICAL_CHUNK_SIZE = 32;
        public const int SIZE = PHYSICAL_CHUNK_SIZE + 2; // Padded size
        public const int FACE = SIZE * SIZE;
        public const int VOLUME = SIZE * SIZE * SIZE;
        public const int SKIRT_SIZE = SIZE;
        public const int SKIRT_FACE = SKIRT_SIZE * SKIRT_SIZE;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 To3DIndex(int index, int3 chunkSize)
        {
            int z = index % chunkSize.z;
            int y = (index / chunkSize.z) % chunkSize.y;
            int x = index / (chunkSize.y * chunkSize.z);
            return new int3(x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int To1DIndex(int3 index, int3 chunkSize)
        {
            return index.x * chunkSize.y * chunkSize.z + index.y * chunkSize.z + index.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int To1DIndex(uint3 index, int3 chunkSize)
        {
            return (int)(index.x * chunkSize.y * chunkSize.z + index.y * chunkSize.z + index.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 WorldToChunk(float3 worldPosition, int3 chunkSize)
        {
            return (int3)math.floor(worldPosition / chunkSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 ChunkToWorld(int3 chunkPosition, int3 chunkSize)
        {
            return chunkPosition * chunkSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundaryCheck(int3 position, int3 chunkSize)
        {
            return math.all(position >= 0 & position < chunkSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 IndexToPos(int index, int size)
        {
            int y = index / (size * size);
            int w = index % (size * size);
            int z = w / size;
            int x = w % size;
            return (uint3)new int3(x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PosToIndex(uint3 position, int size)
        {
            return (int)(position.y * size * size + (position.z * size) + position.x);
        }

        // --- [新增] 遵循目标框架，添加更多几何常量和工具函数 ---
        public static readonly int3[] DC_VERT =
        {
            new int3(0, 0, 0), new int3(1, 0, 0), new int3(1, 1, 0), new int3(0, 1, 0),
            new int3(0, 0, 1), new int3(1, 0, 1), new int3(1, 1, 1), new int3(0, 1, 1)
        };

        public static readonly int[,] DC_EDGE =
        {
            {0, 1}, {1, 2}, {2, 3}, {3, 0},
            {4, 5}, {5, 6}, {6, 7}, {7, 4},
            {0, 4}, {1, 5}, {2, 6}, {3, 7}
        };

        public static readonly int3[] DC_AXES = { new int3(1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1) };

        public static readonly int3[,] DC_ADJACENT =
        {
            { new int3(0, 0, 0), new int3(0, 0, -1), new int3(0, -1, -1), new int3(0, -1, 0) },
            { new int3(0, 0, 0), new int3(-1, 0, 0), new int3(-1, 0, -1), new int3(0, 0, -1) },
            { new int3(0, 0, 0), new int3(0, -1, 0), new int3(-1, -1, 0), new int3(-1, 0, 0) }
        };

        public static readonly float3[] CubeVertices =
        {
            new float3(0f, 0f, 0f), new float3(1f, 0f, 0f), new float3(1f, 1f, 0f), new float3(0f, 1f, 0f),
            new float3(0f, 0f, 1f), new float3(1f, 0f, 1f), new float3(1f, 1f, 1f), new float3(0f, 1f, 1f)
        };

        public static readonly int[] CubeFaces =
        {
            1, 5, 6, 2, // Right
            4, 0, 3, 7, // Left
            3, 2, 6, 7, // Top
            4, 5, 1, 0, // Bottom
            5, 4, 7, 6, // Front
            0, 1, 2, 3  // Back
        };

        public static readonly float2[] CubeUVs =
        {
            new float2(0f, 0f), new float2(1f, 0f), new float2(1f, 1f), new float2(0f, 1f)
        };

        public static readonly int[] CubeIndices =
        {
            0, 2, 1, 0, 3, 2, // [修正] 修正为正确的绕序
            4, 5, 6, 4, 6, 7,
            // ... etc for all 6 faces
        };

        public static readonly int3[] VoxelDirectionOffsets =
        {
            new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0),
            new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1)
        };
    }
}