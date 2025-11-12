using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    [BurstCompile]
    public struct SkirtCopyVertexIndicesJob : IJob
    {
        [ReadOnly] public NativeArray<int> SourceVertexIndices;
        [WriteOnly] public NativeArray<int> SkirtVertexIndicesCopied;
        [ReadOnly] public int3 PaddedChunkSize;

        public void Execute()
        {
            int faceArea = PaddedChunkSize.x * PaddedChunkSize.y;
            for (int face = 0; face < 6; face++)
            {
                uint missing = face < 3 ? 0u : (uint)PaddedChunkSize.x - 3;
                int faceElementOffset = face * faceArea;

                for (int i = 0; i < faceArea; i++)
                {
                    uint2 flattened = (uint2)VoxelUtil.To3DIndex(i, new int3(PaddedChunkSize.x, PaddedChunkSize.y, 1)).xy;
                    uint3 position = VoxelUtil.UnflattenFromFaceRelative(flattened, face % 3, missing);
                    int srcIndex = SourceVertexIndices[VoxelUtil.To1DIndex(position, PaddedChunkSize)];

                    SkirtVertexIndicesCopied[i + faceElementOffset] = srcIndex;
                }
            }
        }
    }
}