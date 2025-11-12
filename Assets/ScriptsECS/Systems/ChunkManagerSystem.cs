using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class ChunkManagerSystem : SystemBase
{
    private EntityQuery existingChunksQuery;
    private int3 lastPlayerChunkPos = new int3(int.MinValue);

    protected override void OnCreate()
    {
        existingChunksQuery = GetEntityQuery(typeof(Chunk));
        RequireForUpdate<PlayerTag>();
        RequireForUpdate<TerrainConfig>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();

        if (math.any(config.ChunkSize <= 0))
        {
            Debug.LogError("TerrainConfig.ChunkSize has zero or negative values, voxel generation cannot continue.");
            Enabled = false;
            return;
        }

        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();

        if (!SystemAPI.HasComponent<LocalToWorld>(playerEntity))
        {
            return;
        }

        var playerTransform = SystemAPI.GetComponent<LocalToWorld>(playerEntity);
        var playerChunkPos = (int3)math.floor(playerTransform.Position / (float3)config.ChunkSize);

        if (math.all(playerChunkPos == lastPlayerChunkPos))
        {
            return;
        }
        lastPlayerChunkPos = playerChunkPos;

        var existingChunkEntities = existingChunksQuery.ToEntityArray(Allocator.Temp);
        var existingChunkComponents = existingChunksQuery.ToComponentDataArray<Chunk>(Allocator.Temp);
        var existingChunks = new NativeHashMap<int3, Entity>(existingChunkEntities.Length, Allocator.Temp);
        for (int i = 0; i < existingChunkEntities.Length; i++)
        {
            existingChunks.TryAdd(existingChunkComponents[i].Position, existingChunkEntities[i]);
        }

        var requiredChunks = new NativeHashSet<int3>(1000, Allocator.Temp);
        var spawnSize = config.ChunkSpawnSize;

        for (int x = -spawnSize.x; x <= spawnSize.x; x++)
            for (int y = -spawnSize.y; y <= spawnSize.y; y++)
                for (int z = -spawnSize.x; z <= spawnSize.x; z++)
                {
                    requiredChunks.Add(playerChunkPos + new int3(x, y, z));
                }

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // [修正] 正确地销毁超出范围的区块并释放其资源
        // 必须使用 `WithEntityAccess` 和 `RefRW` 来获取对组件数据的可写引用，
        // 这样调用 Dispose 才能作用于原始数据而不是副本。
        foreach (var (chunk, voxelData, entity) in SystemAPI.Query<RefRO<Chunk>, RefRW<ChunkVoxelData>>().WithEntityAccess())
        {
            if (!requiredChunks.Contains(chunk.ValueRO.Position))
            {
                voxelData.ValueRW.Dispose(Dependency);
                ecb.DestroyEntity(entity);
            }
        }

        // 销毁可能还没有 `ChunkVoxelData` 的区块
        foreach (var (chunk, entity) in SystemAPI.Query<RefRO<Chunk>>().WithNone<ChunkVoxelData>().WithEntityAccess())
        {
            if (!requiredChunks.Contains(chunk.ValueRO.Position))
            {
                ecb.DestroyEntity(entity);
            }
        }

        // 创建新的区块
        foreach (var pos in requiredChunks)
        {
            if (!existingChunks.ContainsKey(pos))
            {
                Entity newChunk = ecb.CreateEntity();
                ecb.AddComponent(newChunk, new Chunk { Position = pos });
                ecb.AddComponent(newChunk, LocalTransform.FromPosition(pos * config.ChunkSize));
                ecb.AddComponent<LocalToWorld>(newChunk);
                ecb.AddComponent<ChunkVoxelData>(newChunk); // NativeArray is default (not created)
                ecb.AddBuffer<ChunkNeighbor>(newChunk);

                // 添加所有管线标签
                ecb.AddComponent<RequestGpuDataTag>(newChunk);
                ecb.AddComponent<PendingGpuDataTag>(newChunk);
                ecb.AddComponent<RequestPaddingUpdateTag>(newChunk);
                ecb.AddComponent<RequestMeshTag>(newChunk);
                ecb.AddComponent<RequestColliderBakeTag>(newChunk);
                ecb.AddComponent<ChunkModifiedTag>(newChunk);
                ecb.AddComponent<IdleTag>(newChunk);

                // 初始化管线状态
                ecb.SetComponentEnabled<RequestGpuDataTag>(newChunk, true);
                ecb.SetComponentEnabled<PendingGpuDataTag>(newChunk, false);
                ecb.SetComponentEnabled<RequestPaddingUpdateTag>(newChunk, false);
                ecb.SetComponentEnabled<RequestMeshTag>(newChunk, false);
                ecb.SetComponentEnabled<RequestColliderBakeTag>(newChunk, false);
                ecb.SetComponentEnabled<ChunkModifiedTag>(newChunk, false);
                ecb.SetComponentEnabled<IdleTag>(newChunk, false);
            }
        }

        ecb.Playback(EntityManager);

        // 释放临时集合
        existingChunkEntities.Dispose();
        existingChunkComponents.Dispose();
        existingChunks.Dispose();
        requiredChunks.Dispose();
    }
}