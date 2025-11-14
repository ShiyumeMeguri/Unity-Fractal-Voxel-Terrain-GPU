// Systems/ChunkManagerSystem.cs
using Ruri.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct ChunkManagerSystem : ISystem
{
    private Entity _chunkPrototype;
    private bool _prototypesCreated;
    private NativeHashMap<int3, Entity> _chunkMap;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _prototypesCreated = false;
        _chunkMap = new NativeHashMap<int3, Entity>(1024, Allocator.Persistent);

        state.EntityManager.CreateSingleton<TerrainReadySystems>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_chunkMap.IsCreated)
        {
            _chunkMap.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!_prototypesCreated)
        {
            CreatePrototypes(ref state);
        }

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;

        if (!SystemAPI.TryGetSingleton<TerrainConfig>(out var config))
        {
            readySystems.manager = false;
            return;
        }
        if (!SystemAPI.TryGetSingletonEntity<TerrainLoader>(out var loaderEntity))
        {
            readySystems.manager = false;
            return;
        }

        // 更新加载器位置
        var loaderTransform = SystemAPI.GetComponent<LocalToWorld>(loaderEntity);
        ref var loader = ref SystemAPI.GetComponentRW<TerrainLoader>(loaderEntity).ValueRW;
        loader.Position = loaderTransform.Position;

        var playerChunkPos = VoxelUtils.WorldToChunk(loader.Position, config.ChunkSize);

        if (math.all(playerChunkPos == loader.LastChunkPosition))
        {
            readySystems.manager = true; // 假设没有增删操作，管理器就是就绪的
            return;
        }

        loader.LastChunkPosition = playerChunkPos;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        var requiredChunks = new NativeHashSet<int3>(1024, Allocator.Temp);
        var spawnSize = config.ChunkSpawnSize;
        for (int x = -spawnSize.x; x <= spawnSize.x; x++)
            for (int z = -spawnSize.x; z <= spawnSize.x; z++)
            {
                requiredChunks.Add(playerChunkPos + new int3(x, 0, z));
            }

        int destroyCount = 0;
        var keysToRemove = new NativeList<int3>(Allocator.Temp);
        foreach (var pair in _chunkMap)
        {
            if (!requiredChunks.Contains(pair.Key))
            {
                ecb.DestroyEntity(pair.Value);
                keysToRemove.Add(pair.Key);
                destroyCount++;
            }
        }
        foreach (var key in keysToRemove)
        {
            _chunkMap.Remove(key);
        }

        int createCount = 0;
        foreach (var pos in requiredChunks)
        {
            if (!_chunkMap.ContainsKey(pos))
            {
                Entity newChunk = ecb.Instantiate(_chunkPrototype);
                ecb.SetComponent(newChunk, new Chunk { Position = pos });
                ecb.SetComponent(newChunk, LocalTransform.FromPosition(pos * config.ChunkSize));
                _chunkMap.Add(pos, newChunk);
                createCount++;
            }
        }

        readySystems.manager = (createCount == 0 && destroyCount == 0);

        ecb.Playback(state.EntityManager);

        requiredChunks.Dispose();
        keysToRemove.Dispose();
    }

    private void CreatePrototypes(ref SystemState state)
    {
        var mgr = state.EntityManager;
        _chunkPrototype = mgr.CreateEntity();

        mgr.AddComponent<LocalTransform>(_chunkPrototype);
        mgr.AddComponent<LocalToWorld>(_chunkPrototype);
        mgr.AddComponent<Chunk>(_chunkPrototype);
        mgr.AddComponent<TerrainChunkVoxels>(_chunkPrototype);
        mgr.AddComponent<TerrainChunkRequestReadbackTag>(_chunkPrototype);
        mgr.AddComponent<TerrainChunkVoxelsReadyTag>(_chunkPrototype);
        mgr.AddComponent<TerrainChunkRequestMeshingTag>(_chunkPrototype);
        mgr.AddComponent<TerrainChunkMesh>(_chunkPrototype);
        mgr.AddComponent<TerrainChunkRequestCollisionTag>(_chunkPrototype);
        mgr.AddComponent<TerrainChunkEndOfPipeTag>(_chunkPrototype);
        mgr.AddComponent<TerrainDeferredVisible>(_chunkPrototype);
        mgr.AddComponent<Prefab>(_chunkPrototype);

        mgr.SetComponentEnabled<TerrainChunkVoxels>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkMesh>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestMeshingTag>(_chunkPrototype, false); // 创建后先不请求mesh
        mgr.SetComponentEnabled<TerrainChunkRequestCollisionTag>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkEndOfPipeTag>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainDeferredVisible>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestReadbackTag>(_chunkPrototype, true); // 新区块默认请求数据回读

        _prototypesCreated = true;
    }
}