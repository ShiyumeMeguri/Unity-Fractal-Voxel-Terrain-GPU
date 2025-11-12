
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct SkirtClosestSurfaceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Voxel> Voxels;
        [WriteOnly] public NativeArray<bool> WithinThreshold;
        [ReadOnly] public int3 PaddedChunkSize; // 添加字段

        private const int PADDING_SEARCH_AREA = 3;

        public void Execute(int index)
        {
            WithinThreshold[index] = false;
            int faceArea = PaddedChunkSize.x * PaddedChunkSize.y;

            int face = index / faceArea;
            int direction = face % 3;
            bool negative = face < 3;
            int localIndex = index % faceArea;
            uint missing = negative ? 0u : (uint)PaddedChunkSize.x - 2;

            uint2 flattened = (uint2)VoxelUtil.To3DIndex(localIndex, new int3(PaddedChunkSize.x, PaddedChunkSize.y, 1)).xy;
            uint3 position = VoxelUtil.UnflattenFromFaceRelative(flattened, direction, missing);

            if (Voxels[VoxelUtil.To1DIndex(position, PaddedChunkSize)].IsSolid)
            {
                return;
            }

            int2 basePosition2D = (int2)flattened;

            for (int x = -PADDING_SEARCH_AREA; x <= PADDING_SEARCH_AREA; x++)
            {
                for (int y = -PADDING_SEARCH_AREA; y <= PADDING_SEARCH_AREA; y++)
                {
                    int2 offset = new int2(x, y);
                    int3 global = VoxelUtil.UnflattenFromFaceRelative(offset + basePosition2D, direction, (int)missing);

                    if (math.all(global >= 0 & global < PaddedChunkSize.x)) // 修正边界检查
                    {
                        if (Voxels[VoxelUtil.To1DIndex((uint3)global, PaddedChunkSize)].IsSolid)
                        {
                            WithinThreshold[index] = true;
                            return;
                        }
                    }
                }
            }
        }
    }
}