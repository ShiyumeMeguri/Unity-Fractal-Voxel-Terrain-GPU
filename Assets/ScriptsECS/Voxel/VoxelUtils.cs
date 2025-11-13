// Assets/ScriptsECS/Voxel/VoxelUtils.cs
using Unity.Mathematics;

namespace OptIn.Voxel
{
    public static class VoxelUtils
    {
        public const int PHYSICAL_CHUNK_SIZE = 32;
        public const int SIZE = PHYSICAL_CHUNK_SIZE + 2; // Padded size
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
            return (int3)math.floor(worldPosition / chunkSize);
        }

        public static float3 ChunkToWorld(int3 chunkPosition, int3 chunkSize)
        {
            return chunkPosition * chunkSize;
        }

        public static bool BoundaryCheck(int3 position, int3 chunkSize)
        {
            return position.x >= 0 && position.x < chunkSize.x &&
                   position.y >= 0 && position.y < chunkSize.y &&
                   position.z >= 0 && position.z < chunkSize.z;
        }

        public static uint3 IndexToPos(int index, int size)
        {
            int y = index / (size * size);
            int w = index % (size * size);
            int z = w / size;
            int x = w % size;
            return (uint3)new int3(x, y, z);
        }

        public static int PosToIndex(uint3 position, int size)
        {
            return (int)(position.y * size * size + (position.z * size) + position.x);
        }

        public static uint2 IndexToPos2D(int index, int size)
        {
            return new uint2((uint)(index % size), (uint)(index / size));
        }

        public static int PosToIndex2D(uint2 position, int size)
        {
            return (int)(position.x + position.y * size);
        }

        public static int3 UnflattenFromFaceRelative(int2 relative, int dir, int missing = 0)
        {
            if (dir == 0) return new int3(missing, relative.x, relative.y);
            if (dir == 1) return new int3(relative.x, missing, relative.y);
            return new int3(relative.x, relative.y, missing);
        }

        public static uint3 UnflattenFromFaceRelative(uint2 relative, int dir, uint missing = 0)
        {
            if (dir == 0) return new uint3(missing, relative.x, relative.y);
            if (dir == 1) return new uint3(relative.x, missing, relative.y);
            return new uint3(relative.x, relative.y, missing);
        }

        public static float3 UnflattenFromFaceRelative(float2 relative, int dir, float missing = 0)
        {
            if (dir == 0) return new float3(missing, relative.x, relative.y);
            if (dir == 1) return new float3(relative.x, missing, relative.y);
            return new float3(relative.x, relative.y, missing);
        }

        public static int2 FlattenToFaceRelative(int3 position, int dir)
        {
            if (dir == 0) return position.yz;
            if (dir == 1) return position.xz;
            return position.xy;
        }

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

        public static readonly int3[] VoxelDirectionOffsets =
        {
            new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0),
            new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1)
        };
    }
}