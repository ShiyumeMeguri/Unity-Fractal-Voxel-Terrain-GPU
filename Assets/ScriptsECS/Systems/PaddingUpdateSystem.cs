using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using OptIn.Voxel;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VoxelGenerationSystem))]
public partial class PaddingUpdateSystem : SystemBase
{
    private EntityQuery m_PaddingUpdateQuery;
    private EntityQuery m_AllChunksQuery;

    protected override void OnCreate()
    {
        m_PaddingUpdateQuery = GetEntityQuery(ComponentType.ReadOnly<RequestPaddingUpdateTag>(), ComponentType.ReadOnly<Chunk>());
        m_AllChunksQuery = GetEntityQuery(typeof(Chunk), typeof(ChunkVoxelData));
        RequireForUpdate(m_PaddingUpdateQuery);
        RequireForUpdate<TerrainConfig>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        // [修正] 对于在 OnUpdate 中立即使用的 ECB，直接创建临时实例
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        var chunkMap = new NativeHashMap<int3, Entity>(m_AllChunksQuery.CalculateEntityCount(), Allocator.Temp);
        foreach (var (chunk, entity) in SystemAPI.Query<Chunk>().WithEntityAccess().WithAll<ChunkVoxelData>())
        {
            if (SystemAPI.GetComponent<ChunkVoxelData>(entity).IsCreated)
            {
                chunkMap.TryAdd(chunk.Position, entity);
            }
        }

        foreach (var (chunk, entity) in SystemAPI.Query<Chunk>().WithEntityAccess().WithAll<RequestPaddingUpdateTag>())
        {
            var centerVoxelData = SystemAPI.GetComponent<ChunkVoxelData>(entity);
            if (!centerVoxelData.IsCreated) continue;

            bool allNeighborsReady = true;
            for (int j = 0; j < 27; j++)
            {
                if (j == 13) continue;
                var offset = new int3(j % 3 - 1, (j / 3) % 3 - 1, j / 9 - 1);
                var neighborPos = chunk.Position + offset;

                if (!chunkMap.TryGetValue(neighborPos, out var neighborEntity) ||
                    !SystemAPI.GetComponent<ChunkVoxelData>(neighborEntity).IsCreated)
                {
                    allNeighborsReady = false;
                    break;
                }
            }

            if (!allNeighborsReady) continue;

            var centerVoxels = centerVoxelData.Voxels;
            var paddedChunkSize = config.ChunkSize + 2;
            var logicalChunkSize = config.ChunkSize;

            for (int neighborIndex = 0; neighborIndex < 27; neighborIndex++)
            {
                if (neighborIndex == 13) continue;

                var offset = new int3(neighborIndex % 3 - 1, (neighborIndex / 3) % 3 - 1, neighborIndex / 9 - 1);
                var neighborPos = chunk.Position + offset;
                var neighborVoxels = SystemAPI.GetComponent<ChunkVoxelData>(chunkMap[neighborPos]).Voxels;

                int3 src_start = new int3(
                    offset.x == 1 ? 1 : (offset.x == -1 ? logicalChunkSize.x : 1),
                    offset.y == 1 ? 1 : (offset.y == -1 ? logicalChunkSize.y : 1),
                    offset.z == 1 ? 1 : (offset.z == -1 ? logicalChunkSize.z : 1)
                );

                int3 src_end = new int3(
                    offset.x == 1 ? 1 : (offset.x == -1 ? logicalChunkSize.x : logicalChunkSize.x),
                    offset.y == 1 ? 1 : (offset.y == -1 ? logicalChunkSize.y : logicalChunkSize.y),
                    offset.z == 1 ? 1 : (offset.z == -1 ? logicalChunkSize.z : logicalChunkSize.z)
                );

                int3 dst_start = new int3(
                    offset.x == 1 ? paddedChunkSize.x - 1 : (offset.x == -1 ? 0 : 1),
                    offset.y == 1 ? paddedChunkSize.y - 1 : (offset.y == -1 ? 0 : 1),
                    offset.z == 1 ? paddedChunkSize.z - 1 : (offset.z == -1 ? 0 : 1)
                );

                for (int z = src_start.z; z <= src_end.z; z++)
                    for (int y = src_start.y; y <= src_end.y; y++)
                        for (int x = src_start.x; x <= src_end.x; x++)
                        {
                            var src_local = new int3(x, y, z);
                            var dst_local = dst_start + (src_local - src_start);
                            int srcIndex = VoxelUtil.To1DIndex(src_local, paddedChunkSize);
                            int dstIndex = VoxelUtil.To1DIndex(dst_local, paddedChunkSize);
                            centerVoxels[dstIndex] = neighborVoxels[srcIndex];
                        }
            }

            ecb.SetComponentEnabled<RequestPaddingUpdateTag>(entity, false);
            ecb.SetComponentEnabled<RequestMeshTag>(entity, true);
        }

        // [修改] 在 OnUpdate 结束时播放并销毁临时的 ECB
        ecb.Playback(EntityManager);
        ecb.Dispose();
        chunkMap.Dispose();
    }
}