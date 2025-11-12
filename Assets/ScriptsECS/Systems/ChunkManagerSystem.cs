using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct ChunkManagerSystem : ISystem
{
    private int3 lastPlayerChunkPos;
    private EntityQuery existingChunksQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        lastPlayerChunkPos = new int3(int.MinValue);
        existingChunksQuery = state.GetEntityQuery(typeof(Chunk));
        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<TerrainConfig>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Guard Clause: 确保玩家实体存在且有位置信息
        if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity) || !SystemAPI.HasComponent<LocalToWorld>(playerEntity))
        {
            return;
        }

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var playerTransform = SystemAPI.GetComponent<LocalToWorld>(playerEntity);
        var playerChunkPos = (int3)math.floor(playerTransform.Position / (float3)config.ChunkSize);

        // Guard Clause: 如果玩家未移动到新的区块，则不执行任何操作
        if (math.all(playerChunkPos == lastPlayerChunkPos))
        {
            return;
        }

        lastPlayerChunkPos = playerChunkPos;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        var requiredChunks = new NativeHashSet<int3>(1024, Allocator.TempJob);
        var spawnSize = config.ChunkSpawnSize;

        // 计算需要存在的区块位置
        for (int x = -spawnSize.x; x <= spawnSize.x; x++)
            for (int y = -spawnSize.y; y <= spawnSize.y; y++) // 假设y轴也需要生成
                for (int z = -spawnSize.x; z <= spawnSize.x; z++)
                {
                    requiredChunks.Add(playerChunkPos + new int3(x, y, z));
                }

        // 销毁超出范围的区块
        foreach (var (chunk, entity) in SystemAPI.Query<RefRO<Chunk>>().WithEntityAccess())
        {
            if (!requiredChunks.Contains(chunk.ValueRO.Position))
            {
                ecb.DestroyEntity(entity);
            }
        }

        // 创建当前所有区块位置的查找表
        var existingChunksMap = new NativeHashMap<int3, bool>(existingChunksQuery.CalculateEntityCount(), Allocator.TempJob);
        var jobHandle = new FillExistingChunksMapJob
        {
            ChunkMap = existingChunksMap
        }.Schedule(existingChunksQuery, state.Dependency);
        jobHandle.Complete(); // 必须立即完成以供主线程使用

        // 创建新的区块
        var requiredChunksArray = requiredChunks.ToNativeArray(Allocator.Temp);
        foreach (var pos in requiredChunksArray)
        {
            if (existingChunksMap.ContainsKey(pos)) continue;

            Entity newChunk = ecb.CreateEntity();
            ecb.AddComponent(newChunk, new Chunk { Position = pos });
            ecb.AddComponent(newChunk, LocalTransform.FromPosition(pos * config.ChunkSize));
            ecb.AddComponent<LocalToWorld>(newChunk);

            // 初始化管线状态
            ecb.AddComponent<NewChunkTag>(newChunk);
            ecb.AddComponent<RequestGpuDataTag>(newChunk);
            ecb.AddComponent<PendingGpuDataTag>(newChunk);
            ecb.AddComponent<RequestPaddingUpdateTag>(newChunk);
            ecb.AddComponent<RequestMeshTag>(newChunk);
            ecb.AddComponent<PendingMeshTag>(newChunk);
            ecb.AddComponent<RequestColliderBakeTag>(newChunk);
            ecb.AddComponent<IdleTag>(newChunk);
            ecb.AddComponent<ChunkModifiedTag>(newChunk);

            // 设置初始状态：请求GPU数据
            ecb.SetComponentEnabled<NewChunkTag>(newChunk, true);
            ecb.SetComponentEnabled<RequestGpuDataTag>(newChunk, true);
            ecb.SetComponentEnabled<PendingGpuDataTag>(newChunk, false);
            ecb.SetComponentEnabled<RequestPaddingUpdateTag>(newChunk, false);
            ecb.SetComponentEnabled<RequestMeshTag>(newChunk, false);
            ecb.SetComponentEnabled<PendingMeshTag>(newChunk, false);
            ecb.SetComponentEnabled<RequestColliderBakeTag>(newChunk, false);
            ecb.SetComponentEnabled<IdleTag>(newChunk, false);
            ecb.SetComponentEnabled<ChunkModifiedTag>(newChunk, false);
        }

        // 清理临时集合
        requiredChunks.Dispose();
        existingChunksMap.Dispose();
        requiredChunksArray.Dispose();
    }

    [BurstCompile]
    private partial struct FillExistingChunksMapJob : IJobEntity
    {
        public NativeHashMap<int3, bool> ChunkMap;

        public void Execute(in Chunk chunk)
        {
            ChunkMap.TryAdd(chunk.Position, true);
        }
    }
}