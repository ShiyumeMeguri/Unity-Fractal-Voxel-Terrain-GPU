// Utils/EdgePositionUtils.cs
using Unity.Mathematics;

namespace Ruri.Voxel
{
    public static class EdgePositionUtils
    {
        public static readonly uint3[] EDGE_POSITIONS_0 = new uint3[] {
            new uint3(0, 0, 0), new uint3(1, 0, 0), new uint3(1, 1, 0), new uint3(0, 1, 0),
            new uint3(0, 0, 1), new uint3(1, 0, 1), new uint3(1, 1, 1), new uint3(0, 1, 1),
            new uint3(0, 0, 0), new uint3(1, 0, 0), new uint3(1, 1, 0), new uint3(0, 1, 0),
        };

        public static readonly uint3[] EDGE_POSITIONS_1 = new uint3[] {
            new uint3(1, 0, 0), new uint3(1, 1, 0), new uint3(0, 1, 0), new uint3(0, 0, 0),
            new uint3(1, 0, 1), new uint3(1, 1, 1), new uint3(0, 1, 1), new uint3(0, 0, 1),
            new uint3(0, 0, 1), new uint3(1, 0, 1), new uint3(1, 1, 1), new uint3(0, 1, 1),
        };
    }
}