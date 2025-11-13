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
    private int3 _lastPlayerChunkPos;
    private Entity _chunkPrototype;
    private Entity _skirtPrototype;
    private NativeHashMap<int3, Entity> _chunkMap;
    private bool _prototypesCreated;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _lastPlayerChunkPos = new int3(int.MinValue);
        _chunkMap = new NativeHashMap<int3, Entity>(1024, Allocator.Persistent);
        _prototypesCreated = false;

        state.RequireForUpdate<TerrainLoader>();
        state.RequireForUpdate<TerrainConfig>();
        state.EntityManager.CreateSingleton<TerrainReadySystems>();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        state.Dependency.Complete();

        foreach (var pair in _chunkMap)
        {
            if (!state.EntityManager.Exists(pair.Value)) continue;

            if (SystemAPI.HasComponent<TerrainChunkVoxels>(pair.Value) && SystemAPI.IsComponentEnabled<TerrainChunkVoxels>(pair.Value))
            {
                SystemAPI.GetComponent<TerrainChunkVoxels>(pair.Value).Dispose();
            }
            if (SystemAPI.HasComponent<TerrainChunkMesh>(pair.Value) && SystemAPI.IsComponentEnabled<TerrainChunkMesh>(pair.Value))
            {
                SystemAPI.GetComponent<TerrainChunkMesh>(pair.Value).Dispose();
            }
        }
        if (_chunkMap.IsCreated) _chunkMap.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!_prototypesCreated) CreatePrototypes(ref state);

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.manager = true;

        if (!SystemAPI.TryGetSingletonEntity<TerrainLoader>(out var loaderEntity)) return;
        var loaderPos = SystemAPI.GetComponent<LocalToWorld>(loaderEntity).Position;

        // 更新Loader组件
        var loader = SystemAPI.GetComponentRW<TerrainLoader>(loaderEntity);
        loader.ValueRW.Position = loaderPos;

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var playerChunkPos = VoxelUtils.WorldToChunk(loaderPos, config.ChunkSize);

        if (math.all(playerChunkPos == loader.ValueRO.LastChunkPosition))
        {
            UpdateChunkNeighbours(ref state);
            return;
        }

        loader.ValueRW.LastChunkPosition = playerChunkPos;
        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        var requiredChunks = new NativeHashSet<int3>(1024, Allocator.Temp);
        var spawnSize = config.ChunkSpawnSize;

        // Y轴不生成，保持和你OOP逻辑一致
        for (int x = -spawnSize.x; x <= spawnSize.x; x++)
        {
            for (int z = -spawnSize.x; z <= spawnSize.x; z++)
            {
                requiredChunks.Add(playerChunkPos + new int3(x, 0, z)); // 假设Y轴固定在0
            }
        }

        var chunksToDestroy = new NativeList<Entity>(Allocator.Temp);
        var keysToRemove = new NativeList<int3>(Allocator.Temp);
        foreach (var pair in _chunkMap)
        {
            if (!requiredChunks.Contains(pair.Key))
            {
                chunksToDestroy.Add(pair.Value);
                keysToRemove.Add(pair.Key);
            }
        }

        if (chunksToDestroy.Length > 0) readySystems.manager = false;

        foreach (var entity in chunksToDestroy)
        {
            if (SystemAPI.Exists(entity))
            {
                ecb.DestroyEntity(entity);
            }
        }

        foreach (var key in keysToRemove)
        {
            _chunkMap.Remove(key);
        }

        foreach (var pos in requiredChunks)
        {
            if (_chunkMap.ContainsKey(pos)) continue;

            readySystems.manager = false;

            Entity newChunk = ecb.Instantiate(_chunkPrototype);

            var skirts = new FixedList64Bytes<Entity>();
            for (byte i = 0; i < 6; i++)
            {
                var skirt = ecb.Instantiate(_skirtPrototype);
                ecb.SetComponent(skirt, new TerrainSkirt { Direction = i });
                ecb.SetComponent(skirt, new TerrainSkirtLinkedParent { ChunkParent = newChunk });
                ecb.SetComponent(skirt, LocalTransform.FromPosition(pos * config.ChunkSize));
                skirts.Add(skirt);
            }

            ecb.SetComponent(newChunk, new Chunk { Position = pos, Skirts = skirts });
            ecb.SetComponent(newChunk, LocalTransform.FromPosition(pos * config.ChunkSize));
            _chunkMap.TryAdd(pos, newChunk);
        }

        state.Dependency.Complete();
        ecb.Playback(state.EntityManager);

        requiredChunks.Dispose();
        chunksToDestroy.Dispose();
        keysToRemove.Dispose();

        UpdateChunkNeighbours(ref state);
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
        mgr.SetComponentEnabled<TerrainChunkRequestMeshingTag>(_chunkPrototype, true);
        mgr.SetComponentEnabled<TerrainChunkRequestCollisionTag>(_chunkPrototype, true);
        mgr.SetComponentEnabled<TerrainChunkEndOfPipeTag>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainDeferredVisible>(_chunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestReadbackTag>(_chunkPrototype, true);

        _skirtPrototype = mgr.CreateEntity();
        mgr.AddComponent<LocalTransform>(_skirtPrototype);
        mgr.AddComponent<LocalToWorld>(_skirtPrototype);
        mgr.AddComponent<TerrainSkirt>(_skirtPrototype);
        mgr.AddComponent<TerrainDeferredVisible>(_skirtPrototype);
        mgr.AddComponent<TerrainSkirtLinkedParent>(_skirtPrototype);
        mgr.AddComponent<Prefab>(_skirtPrototype);
        mgr.SetComponentEnabled<TerrainDeferredVisible>(_skirtPrototype, false);

        _prototypesCreated = true;
    }

    [BurstCompile]
    private void UpdateChunkNeighbours(ref SystemState state)
    {
        var job = new UpdateNeighbourMasksJob
        {
            ChunkMap = _chunkMap.AsReadOnly()
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Chunk))]
    private partial struct UpdateNeighbourMasksJob : IJobEntity
    {
        [ReadOnly] public NativeHashMap<int3, Entity>.ReadOnly ChunkMap;

        public void Execute(ref Chunk chunk)
        {
            BitField32 neighbourMask = default;
            for (int i = 0; i < 27; i++)
            {
                int3 offset = (int3)VoxelUtils.IndexToPos(i, 3) - 1;
                if (ChunkMap.ContainsKey(chunk.Position + offset))
                {
                    neighbourMask.SetBits(i, true);
                }
            }
            chunk.NeighbourMask = neighbourMask;
            chunk.SkirtMask = CalculateEnabledSkirtMask(neighbourMask);
        }
    }

    private static byte CalculateEnabledSkirtMask(BitField32 inputMask)
    {
        byte outputMask = 0;
        for (int i = 0; i < 27; i++)
        {
            if (!inputMask.IsSet(i))
            {
                uint3 offset = VoxelUtils.IndexToPos(i, 3);
                byte backing = 0;
                BitUtils.SetBit(ref backing, 0, offset.x == 0);
                BitUtils.SetBit(ref backing, 1, offset.y == 0);
                BitUtils.SetBit(ref backing, 2, offset.z == 0);
                BitUtils.SetBit(ref backing, 3, offset.x == 2);
                BitUtils.SetBit(ref backing, 4, offset.y == 2);
                BitUtils.SetBit(ref backing, 5, offset.z == 2);
                outputMask |= backing;
            }
        }
        return outputMask;
    }
}