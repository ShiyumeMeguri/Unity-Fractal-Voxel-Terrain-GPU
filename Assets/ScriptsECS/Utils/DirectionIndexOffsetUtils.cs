using Unity.Mathematics;
using OptIn.Voxel;

public static class DirectionIndexOffsetUtils
{
    public static readonly int4[] PERPENDICULAR_OFFSETS_INDEX_OFFSET = new int4[] {
        new int4(
            PosToIndex(new uint3(1, 0, 0) + new uint3(0, 0, 0)),
            PosToIndex(new uint3(1, 0, 0) + new uint3(0, 1, 0)),
            PosToIndex(new uint3(1, 0, 0) + new uint3(0, 1, 1)),
            PosToIndex(new uint3(1, 0, 0) + new uint3(0, 0, 1))
        ),

        new int4(
            PosToIndex(new uint3(0, 1, 0) + new uint3(0, 0, 0)),
            PosToIndex(new uint3(0, 1, 0) + new uint3(0, 0, 1)),
            PosToIndex(new uint3(0, 1, 0) + new uint3(1, 0, 1)),
            PosToIndex(new uint3(0, 1, 0) + new uint3(1, 0, 0))
        ),

        new int4(
            PosToIndex(new uint3(0, 0, 1) + new uint3(0, 0, 0)),
            PosToIndex(new uint3(0, 0, 1) + new uint3(1, 0, 0)),
            PosToIndex(new uint3(0, 0, 1) + new uint3(1, 1, 0)),
            PosToIndex(new uint3(0, 0, 1) + new uint3(0, 1, 0))
        ),
    };
    
    public static readonly int[] FORWARD_DIRECTION_INDEX_OFFSET = new int[] {
        1, VoxelUtils.SIZE*VoxelUtils.SIZE, VoxelUtils.SIZE
    };
    
    public static readonly int NEGATIVE_ONE_OFFSET = -(1 + VoxelUtils.SIZE + VoxelUtils.SIZE * VoxelUtils.SIZE);

    private static int PosToIndex(uint3 pos)
    {
        return VoxelUtils.PosToIndex(pos, VoxelUtils.SIZE);
    }
}