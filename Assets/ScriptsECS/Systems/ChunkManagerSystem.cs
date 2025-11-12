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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        lastPlayerChunkPos = new int3(int.MinValue);
        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<TerrainConfig>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();

        if (!SystemAPI.HasComponent<LocalToWorld>(playerEntity)) return;

        var playerTransform = SystemAPI.GetComponent<LocalToWorld>(playerEntity);
        var playerChunkPos = (int3)math.floor(playerTransform.Position / (float3)config.ChunkSize);

        if (math.all(playerChunkPos == lastPlayerChunkPos)) return;

        lastPlayerChunkPos = playerChunkPos;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        var requiredChunks = new NativeHashSet<int3>(1024, Allocator.TempJob);
        var spawnSize = config.ChunkSpawnSize;

        for (int x = -spawnSize.x; x <= spawnSize.x; x++)
            for (int y = -spawnSize.y; y <= spawnSize.y; y++)
                for (int z = -spawnSize.x; z <= spawnSize.x; z++)
                {
                    requiredChunks.Add(playerChunkPos + new int3(x, y, z));
                }

        var existingChunksMap = new NativeHashMap<int3, bool>(requiredChunks.Count, Allocator.TempJob);

        // 销毁超出范围的区块
        foreach (var (chunk, entity) in SystemAPI.Query<RefRO<Chunk>>().WithEntityAccess())
        {
            if (!requiredChunks.Contains(chunk.ValueRO.Position))
            {
                ecb.DestroyEntity(entity);
            }
            else
            {
                existingChunksMap.TryAdd(chunk.ValueRO.Position, true);
            }
        }

        // 创建新的区块
        var requiredChunksArray = requiredChunks.ToNativeArray(Allocator.TempJob);
        foreach (var pos in requiredChunksArray)
        {
            if (existingChunksMap.ContainsKey(pos)) continue;

            Entity newChunk = ecb.CreateEntity();
            ecb.AddComponent(newChunk, new Chunk { Position = pos });
            ecb.AddComponent(newChunk, LocalTransform.FromPosition(pos * config.ChunkSize));
            ecb.AddComponent<LocalToWorld>(newChunk);
            ecb.AddComponent<ChunkVoxelData>(newChunk);

            ecb.AddComponent<NewChunkTag>(newChunk);
            ecb.AddComponent<RequestGpuDataTag>(newChunk);
            ecb.AddComponent<PendingGpuDataTag>(newChunk);
            ecb.AddComponent<RequestPaddingUpdateTag>(newChunk);
            ecb.AddComponent<RequestMeshTag>(newChunk);
            ecb.AddComponent<PendingMeshTag>(newChunk);
            ecb.AddComponent<RequestColliderBakeTag>(newChunk);
            ecb.AddComponent<IdleTag>(newChunk);
            ecb.AddComponent<ChunkModifiedTag>(newChunk);

            ecb.SetComponentEnabled<RequestGpuDataTag>(newChunk, true);
            ecb.SetComponentEnabled<PendingGpuDataTag>(newChunk, false);
            ecb.SetComponentEnabled<RequestPaddingUpdateTag>(newChunk, false);
            ecb.SetComponentEnabled<RequestMeshTag>(newChunk, false);
            ecb.SetComponentEnabled<PendingMeshTag>(newChunk, false);
            ecb.SetComponentEnabled<RequestColliderBakeTag>(newChunk, false);
            ecb.SetComponentEnabled<IdleTag>(newChunk, false);
            ecb.SetComponentEnabled<ChunkModifiedTag>(newChunk, false);
        }

        state.Dependency = requiredChunks.Dispose(state.Dependency);
        state.Dependency = existingChunksMap.Dispose(state.Dependency);
        state.Dependency = requiredChunksArray.Dispose(state.Dependency);
    }
}