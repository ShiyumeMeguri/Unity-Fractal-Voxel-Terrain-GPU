using OptIn.Voxel;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VoxelGenerationSystem))]
[BurstCompile]
public partial struct PaddingUpdateSystem : ISystem
{
    private EntityQuery m_AllReadyChunksQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        m_AllReadyChunksQuery = state.GetEntityQuery(typeof(Chunk), typeof(ChunkVoxelData));
        state.RequireForUpdate<TerrainConfig>();
        state.RequireForUpdate(state.GetEntityQuery(ComponentType.ReadOnly<RequestPaddingUpdateTag>()));
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var chunkMap = new NativeHashMap<int3, Entity>(m_AllReadyChunksQuery.CalculateEntityCount(), Allocator.TempJob);
        var chunkDataLookup = SystemAPI.GetComponentLookup<ChunkVoxelData>(true);

        var fillMapJobHandle = new FillChunkMapJob
        {
            ChunkMap = chunkMap,
            ChunkDataLookup = chunkDataLookup,
            ChunkHandle = SystemAPI.GetComponentTypeHandle<Chunk>(true),
            EntityHandle = SystemAPI.GetEntityTypeHandle(),
        }.Schedule(m_AllReadyChunksQuery, state.Dependency);

        var updatePaddingJob = new UpdatePaddingJob
        {
            ECB = ecb,
            ChunkMap = chunkMap.AsReadOnly(),
            ChunkDataLookup = SystemAPI.GetComponentLookup<ChunkVoxelData>(false),
            ReadOnlyChunkDataLookup = chunkDataLookup,
            PaddedChunkSize = config.ChunkSize + 2,
            LogicalChunkSize = config.ChunkSize,
        }.ScheduleParallel(fillMapJobHandle);

        // 修正：确保在 updatePaddingJob 完成后才释放 chunkMap
        state.Dependency = chunkMap.Dispose(updatePaddingJob);
    }

    [BurstCompile]
    private struct FillChunkMapJob : IJobChunk
    {
        public NativeHashMap<int3, Entity> ChunkMap;
        [ReadOnly] public ComponentLookup<ChunkVoxelData> ChunkDataLookup;
        [ReadOnly] public ComponentTypeHandle<Chunk> ChunkHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            var chunks = chunk.GetNativeArray(ChunkHandle);
            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                if (ChunkDataLookup.HasComponent(entity) && ChunkDataLookup[entity].IsCreated)
                {
                    ChunkMap.TryAdd(chunks[i].Position, entity);
                }
            }
        }
    }

    [BurstCompile]
    [WithAll(typeof(RequestPaddingUpdateTag))]
    private partial struct UpdatePaddingJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public NativeHashMap<int3, Entity>.ReadOnly ChunkMap;
        [NativeDisableParallelForRestriction] public ComponentLookup<ChunkVoxelData> ChunkDataLookup;
        [ReadOnly] public ComponentLookup<ChunkVoxelData> ReadOnlyChunkDataLookup;
        public int3 PaddedChunkSize;
        public int3 LogicalChunkSize;

        public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex, [ReadOnly] ref Chunk chunk)
        {
            var centerVoxelData = ChunkDataLookup[entity];
            if (!centerVoxelData.IsCreated) return;

            for (int i = 0; i < 27; i++)
            {
                if (i == 13) continue;
                var offset = new int3(i % 3 - 1, (i / 3) % 3 - 1, i / 9 - 1);
                var neighborPos = chunk.Position + offset;
                if (!ChunkMap.ContainsKey(neighborPos))
                {
                    return;
                }
            }

            var centerVoxels = centerVoxelData.Voxels;

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        var offset = new int3(dx, dy, dz);
                        var neighborPos = chunk.Position + offset;
                        var neighborVoxels = ReadOnlyChunkDataLookup[ChunkMap[neighborPos]].Voxels;
                        CopyPadding(centerVoxels, neighborVoxels, offset);
                    }

            ECB.SetComponentEnabled<RequestPaddingUpdateTag>(chunkIndex, entity, false);
            ECB.SetComponentEnabled<RequestMeshTag>(chunkIndex, entity, true);
        }

        private void CopyPadding(NativeArray<Voxel> centerVoxels, [ReadOnly] NativeArray<Voxel> neighborVoxels, int3 offset)
        {
            // [修复] 通过显式类型转换解决 math.select 的歧义
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
}