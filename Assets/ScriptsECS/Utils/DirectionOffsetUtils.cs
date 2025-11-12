// Assets/ScriptsECS/Utils/DirectionOffsetUtils.cs
using Unity.Mathematics;

namespace OptIn.Voxel
{
    public static class DirectionOffsetUtils
    {
        public static readonly uint3[] FORWARD_DIRECTION = new uint3[] {
            new uint3(1, 0, 0),
            new uint3(0, 1, 0),
            new uint3(0, 0, 1),
        };

        public static readonly int3[] FORWARD_DIRECTION_INCLUDING_NEGATIVE = new int3[] {
            new int3(-1, 0, 0),
            new int3(0, -1, 0),
            new int3(0, 0, -1),
            new int3(1, 0, 0),
            new int3(0, 1, 0),
            new int3(0, 0, 1),
        };

        public static readonly uint3[] PERPENDICULAR_OFFSETS = new uint3[] {
            new uint3(0, 0, 0),
            new uint3(0, 1, 0),
            new uint3(0, 1, 1),
            new uint3(0, 0, 1),

            new uint3(0, 0, 0),
            new uint3(0, 0, 1),
            new uint3(1, 0, 1),
            new uint3(1, 0, 0),

            new uint3(0, 0, 0),
            new uint3(1, 0, 0),
            new uint3(1, 1, 0),
            new uint3(0, 1, 0)
        };
    }
}