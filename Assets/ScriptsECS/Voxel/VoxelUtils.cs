using Unity.Mathematics;

namespace OptIn.Voxel
{
    public static class VoxelUtils
    {
        // --- 核心常量，与参考框架对齐 ---
        public const int SIZE = 34; // 对应 PaddedChunkSize 的维度
        public const int FACE = SIZE * SIZE;
        public const int VOLUME = SIZE * SIZE * SIZE;
        public const int SKIRT_SIZE = SIZE;
        public const int SKIRT_FACE = SKIRT_SIZE * SKIRT_SIZE;

        public static int3 To3DIndex(int index, int3 chunkSize)
        {
            int z = index % chunkSize.z;
            int y = (index / chunkSize.z) % chunkSize.y;
            int x = index / (chunkSize.y * chunkSize.z);
            return new int3(x, y, z);
        }

        public static int To1DIndex(int3 index, int3 chunkSize)
        {
            return index.x * chunkSize.y * chunkSize.z + index.y * chunkSize.z + index.z;
        }

        public static int To1DIndex(uint3 index, int3 chunkSize)
        {
            return (int)(index.x * chunkSize.y * chunkSize.z + index.y * chunkSize.z + index.z);
        }

        public static int3 WorldToChunk(float3 worldPosition, int3 chunkSize)
        {
            return new int3(
                Floor(worldPosition.x / chunkSize.x),
                Floor(worldPosition.y / chunkSize.y),
                Floor(worldPosition.z / chunkSize.z)
            );
        }

        public static bool BoundaryCheck(int3 position, int3 chunkSize)
        {
            return position.x >= 0 && position.x < chunkSize.x &&
                   position.y >= 0 && position.y < chunkSize.y &&
                   position.z >= 0 && position.z < chunkSize.z;
        }

        public static bool BoundaryCheck(uint3 position, int3 chunkSize)
        {
            return position.x < chunkSize.x &&
                   position.y < chunkSize.y &&
                   position.z < chunkSize.z;
        }

        public static int Floor(float x)
        {
            int xi = (int)x;
            return x < xi ? xi - 1 : xi;
        }

        public static uint2 To2DIndex(int index, int size)
        {
            return new uint2((uint)(index % size), (uint)(index / size));
        }

        public static int To1DIndex(uint2 pos, int size)
        {
            return (int)(pos.x + pos.y * size);
        }

        // --- Skirt 相关函数 ---
        public static uint3 UnflattenFromFaceRelative(uint2 relative, int dir, uint missing = 0)
        {
            if (dir == 0) return new uint3(missing, relative.x, relative.y);
            if (dir == 1) return new uint3(relative.x, missing, relative.y);
            return new uint3(relative.x, relative.y, missing);
        }

        public static int3 UnflattenFromFaceRelative(int2 relative, int dir, int missing = 0)
        {
            if (dir == 0) return new int3(missing, relative.x, relative.y);
            if (dir == 1) return new int3(relative.x, missing, relative.y);
            return new int3(relative.x, relative.y, missing);
        }

        public static int2 FlattenToFaceRelative(int3 position, int dir)
        {
            if (dir == 0) return position.yz;
            if (dir == 1) return position.xz;
            return position.xy;
        }

        // --- Block Meshing Constants ---
        public static readonly int[] DirectionAlignedX = { 2, 2, 0, 0, 0, 0 };
        public static readonly int[] DirectionAlignedY = { 1, 1, 2, 2, 1, 1 };

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

        // --- Dual Contouring Constants ---
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

        // --- General Direction Constants ---
        public static readonly int3[] VoxelDirectionOffsets =
        {
            new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0),
            new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1)
        };
    }
}