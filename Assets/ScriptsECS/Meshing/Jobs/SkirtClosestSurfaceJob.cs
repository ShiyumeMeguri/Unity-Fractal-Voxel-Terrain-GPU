// Assets/ScriptsECS/Meshing/Jobs/SkirtClosestSurfaceJob.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct SkirtClosestSurfaceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<VoxelData> Voxels;
        [WriteOnly] public NativeArray<bool> WithinThreshold;
        [ReadOnly] public int3 PaddedChunkSize;

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

            // [修复] VoxelUtils.To3DIndex 返回 int3, 其 .xy 是 int2, 需要强制转换为 uint2
            uint2 flattened = (uint2)VoxelUtils.To3DIndex(localIndex, new int3(PaddedChunkSize.x, PaddedChunkSize.y, 1)).xy;
            uint3 position = VoxelUtils.UnflattenFromFaceRelative(flattened, direction, missing);

            if (Voxels[VoxelUtils.To1DIndex(position, PaddedChunkSize)].IsSolid)
            {
                return;
            }

            int2 basePosition2D = (int2)flattened;

            for (int x = -PADDING_SEARCH_AREA; x <= PADDING_SEARCH_AREA; x++)
            {
                for (int y = -PADDING_SEARCH_AREA; y <= PADDING_SEARCH_AREA; y++)
                {
                    int2 offset = new int2(x, y);
                    int3 global = VoxelUtils.UnflattenFromFaceRelative(offset + basePosition2D, direction, (int)missing);

                    // [修复] 边界检查应使用 PaddedChunkSize
                    if (math.all(global >= 0 & global < PaddedChunkSize))
                    {
                        if (Voxels[VoxelUtils.To1DIndex((uint3)global, PaddedChunkSize)].IsSolid)
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