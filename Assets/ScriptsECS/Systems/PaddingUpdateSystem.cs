using OptIn.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GpuReadbackSystem))]
[BurstCompile]
public partial struct PaddingUpdateSystem : ISystem
{
    private EntityQuery m_AllReadyChunksQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // 查询所有已完成GPU数据生成的区块
        m_AllReadyChunksQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<Chunk>(),
            ComponentType.ReadOnly<ChunkVoxelData>(),
            ComponentType.Exclude<NewChunkTag>(),
            ComponentType.Exclude<RequestGpuDataTag>(),
            ComponentType.Exclude<PendingGpuDataTag>()
        );
        state.RequireForUpdate<TerrainConfig>();
        state.RequireForUpdate(state.GetEntityQuery(ComponentType.ReadOnly<RequestPaddingUpdateTag>()));
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var chunkMap = new NativeHashMap<int3, Entity>(m_AllReadyChunksQuery.CalculateEntityCount(), Allocator.TempJob);

        var jobHandle = new FillChunkMapJob
        {
            ChunkMap = chunkMap,
        }.Schedule(m_AllReadyChunksQuery, state.Dependency);

        jobHandle = new UpdatePaddingJob
        {
            ECB = ecb,
            ChunkMap = chunkMap,
            ChunkDataLookup = SystemAPI.GetComponentLookup<ChunkVoxelData>(false),
            PaddedChunkSize = config.ChunkSize + 2,
            LogicalChunkSize = config.ChunkSize,
        }.Schedule(jobHandle);

        state.Dependency = chunkMap.Dispose(jobHandle);
    }

    [BurstCompile]
    private partial struct FillChunkMapJob : IJobEntity
    {
        public NativeHashMap<int3, Entity> ChunkMap;

        public void Execute(Entity entity, [ReadOnly] in Chunk chunk)
        {
            ChunkMap.TryAdd(chunk.Position, entity);
        }
    }

    [BurstCompile]
    [WithAll(typeof(RequestPaddingUpdateTag))]
    private partial struct UpdatePaddingJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public NativeHashMap<int3, Entity> ChunkMap;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<ChunkVoxelData> ChunkDataLookup;

        public int3 PaddedChunkSize;
        public int3 LogicalChunkSize;

        public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, [ReadOnly] ref Chunk chunk)
        {
            // 检查所有邻居是否都准备好了
            for (int i = 0; i < 27; i++)
            {
                if (i == 13) continue;
                var offset = new int3(i % 3 - 1, (i / 3) % 3 - 1, i / 9 - 1);
                var neighborPos = chunk.Position + offset;
                if (!ChunkMap.ContainsKey(neighborPos))
                {
                    // 如果有任何一个邻居还没准备好，就直接返回，下一帧再试
                    return;
                }
            }

            var centerVoxelData = ChunkDataLookup[entity];
            if (!centerVoxelData.IsCreated) return;

            var centerVoxels = centerVoxelData.Voxels;

            // 复制所有邻居的边界体素
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        var offset = new int3(dx, dy, dz);
                        var neighborPos = chunk.Position + offset;
                        var neighborVoxels = ChunkDataLookup[ChunkMap[neighborPos]].Voxels;

                        CopyPadding(centerVoxels, neighborVoxels, offset);
                    }

            // 更新状态，进入网格生成阶段
            ECB.SetComponentEnabled<RequestPaddingUpdateTag>(chunkIndex, entity, false);
            ECB.SetComponentEnabled<RequestMeshTag>(chunkIndex, entity, true);
        }

        private void CopyPadding(NativeArray<Voxel> centerVoxels, [ReadOnly] NativeArray<Voxel> neighborVoxels, int3 offset)
        {
            int3 src_start = new int3(
                offset.x == 1 ? 1 : (offset.x == -1 ? LogicalChunkSize.x : 1),
                offset.y == 1 ? 1 : (offset.y == -1 ? LogicalChunkSize.y : 1),
                offset.z == 1 ? 1 : (offset.z == -1 ? LogicalChunkSize.z : 1)
            );

            int3 src_end = new int3(
                offset.x == 1 ? 1 : (offset.x == -1 ? LogicalChunkSize.x : LogicalChunkSize.x),
                offset.y == 1 ? 1 : (offset.y == -1 ? LogicalChunkSize.y : LogicalChunkSize.y),
                offset.z == 1 ? 1 : (offset.z == -1 ? LogicalChunkSize.z : LogicalChunkSize.z)
            );

            int3 dst_start = new int3(
                offset.x == 1 ? PaddedChunkSize.x - 1 : (offset.x == -1 ? 0 : 1),
                offset.y == 1 ? PaddedChunkSize.y - 1 : (offset.y == -1 ? 0 : 1),
                offset.z == 1 ? PaddedChunkSize.z - 1 : (offset.z == -1 ? 0 : 1)
            );

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
}