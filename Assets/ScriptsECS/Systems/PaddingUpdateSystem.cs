using OptIn.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VoxelGenerationSystem))]
[BurstCompile]
public partial class PaddingUpdateSystem : SystemBase
{
    private EntityQuery m_RequestingChunksQuery;
    private EntityQuery m_AllChunksQuery;
    private EndSimulationEntityCommandBufferSystem m_EndSimECBSystem;

    protected override void OnCreate()
    {
        m_RequestingChunksQuery = GetEntityQuery(ComponentType.ReadOnly<Chunk>(), ComponentType.ReadOnly<RequestPaddingUpdateTag>());
        m_AllChunksQuery = GetEntityQuery(ComponentType.ReadOnly<Chunk>());
        m_EndSimECBSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

        RequireForUpdate<TerrainConfig>();
        RequireForUpdate(m_RequestingChunksQuery);
    }

    protected override void OnUpdate()
    {
        // 确保之前的Job已完成，这样我们可以安全地访问组件数据
        CompleteDependency();

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = m_EndSimECBSystem.CreateCommandBuffer();

        // 在主线程上创建查找表
        var chunkMap = new NativeHashMap<int3, Entity>(m_AllChunksQuery.CalculateEntityCount(), Allocator.Temp);
        foreach (var (chunk, entity) in SystemAPI.Query<RefRO<Chunk>>().WithEntityAccess())
        {
            chunkMap.TryAdd(chunk.ValueRO.Position, entity);
        }

        var chunkDataLookup = SystemAPI.GetComponentLookup<ChunkVoxelData>(true); // 只读用于检查邻居
        var chunkDataWriter = SystemAPI.GetComponentLookup<ChunkVoxelData>(false); // 读写用于修改中心区块

        // [修复] 将所有逻辑移至主线程的 foreach 循环中
        foreach (var entity in m_RequestingChunksQuery.ToEntityArray(Allocator.Temp))
        {
            var chunk = SystemAPI.GetComponent<Chunk>(entity);

            if (!chunkDataLookup.HasComponent(entity) || !chunkDataLookup[entity].IsCreated) continue;

            // 检查所有邻居是否都已准备好
            bool allNeighborsReady = true;
            for (int i = 0; i < 27; i++)
            {
                if (i == 13) continue;
                var offset = new int3(i % 3 - 1, (i / 3) % 3 - 1, i / 9 - 1);
                var neighborPos = chunk.Position + offset;

                if (!chunkMap.TryGetValue(neighborPos, out var neighborEntity) ||
                    !chunkDataLookup.HasComponent(neighborEntity) ||
                    !chunkDataLookup[neighborEntity].IsCreated)
                {
                    allNeighborsReady = false;
                    break;
                }
            }

            if (!allNeighborsReady) continue;

            // 如果所有邻居都准备好了，则执行 Padding 复制
            var centerVoxels = chunkDataWriter[entity].Voxels;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        var offset = new int3(dx, dy, dz);
                        var neighborPos = chunk.Position + offset;
                        var neighborVoxels = chunkDataLookup[chunkMap[neighborPos]].Voxels;
                        CopyPadding(centerVoxels, neighborVoxels, offset, config.ChunkSize + 2, config.ChunkSize);
                    }

            // 更新组件状态
            ecb.SetComponentEnabled<RequestPaddingUpdateTag>(entity, false);
            ecb.SetComponentEnabled<RequestMeshTag>(entity, true);
        }

        chunkMap.Dispose();
        m_EndSimECBSystem.AddJobHandleForProducer(Dependency);
    }

    // 将此方法设为静态，因为它不再是作业的一部分
    private static void CopyPadding(NativeArray<Voxel> centerVoxels, [ReadOnly] NativeArray<Voxel> neighborVoxels, int3 offset, int3 PaddedChunkSize, int3 LogicalChunkSize)
    {
        int3 src_start = math.select((int3)1, new int3(LogicalChunkSize.x, LogicalChunkSize.y, LogicalChunkSize.z), offset == -1);
        src_start = math.select(src_start, (int3)1, offset == 1);

        int3 src_end = math.select(LogicalChunkSize, (int3)1, offset == 1);
        src_end = math.select(src_end, LogicalChunkSize, offset == -1);
        src_end = math.select(src_end, LogicalChunkSize, offset == 0);

        int3 dst_start = math.select((int3)1, (int3)0, offset == -1);
        dst_start = math.select(dst_start, PaddedChunkSize - 1, offset == 1);

        for (int z = src_start.z; z <= src_end.z; z++)
            for (int y = src_start.y; y <= src_end.y; y++)
                for (int x = src_start.x; x <= src_end.x; x++)
                {
                    var src_local = new int3(x, y, z);
                    var dst_local = dst_start + (src_local - src_start);
                    int srcIndex = VoxelUtil.To1DIndex(src_local, PaddedChunkSize);
                    int dstIndex = VoxelUtil.To1DIndex(dst_local, PaddedChunkSize);
                    centerVoxels[dstIndex] = neighborVoxels[srcIndex];
                }
    }
}