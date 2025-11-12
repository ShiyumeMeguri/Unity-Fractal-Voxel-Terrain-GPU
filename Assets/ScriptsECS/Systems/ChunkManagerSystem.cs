// Assets/ScriptsECS/Systems/ChunkManagerSystem.cs

using OptIn.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[BurstCompile]
public partial struct ChunkManagerSystem : ISystem
{
    private int3 _LastPlayerChunkPos;
    private Entity _ChunkPrototype;
    private Entity _SkirtPrototype;
    private NativeHashMap<int3, Entity> _ChunkMap;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _LastPlayerChunkPos = new int3(int.MinValue);
        _ChunkMap = new NativeHashMap<int3, Entity>(1024, Allocator.Persistent);

        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<TerrainConfig>();

        CreatePrototypes(ref state);

        state.EntityManager.CreateSingleton<TerrainReadySystems>();
    }

    private void CreatePrototypes(ref SystemState state)
    {
        var mgr = state.EntityManager;
        _ChunkPrototype = mgr.CreateEntity();
        mgr.AddComponent<LocalTransform>(_ChunkPrototype); // 修复：添加 LocalTransform
        mgr.AddComponent<LocalToWorld>(_ChunkPrototype);
        mgr.AddComponent<Chunk>(_ChunkPrototype);
        // Do not add ChunkVoxelData here, it will be added on demand.
        mgr.AddComponent<TerrainChunkRequestReadbackTag>(_ChunkPrototype);
        mgr.AddComponent<TerrainChunkVoxelsReadyTag>(_ChunkPrototype);
        mgr.AddComponent<TerrainChunkRequestMeshingTag>(_ChunkPrototype);
        mgr.AddComponent<TerrainChunkMesh>(_ChunkPrototype);
        mgr.AddComponent<TerrainChunkRequestCollisionTag>(_ChunkPrototype);
        mgr.AddComponent<TerrainChunkEndOfPipeTag>(_ChunkPrototype);
        mgr.AddComponent<TerrainDeferredVisible>(_ChunkPrototype);
        mgr.AddComponent<Prefab>(_ChunkPrototype);

        mgr.SetComponentEnabled<TerrainChunkMesh>(_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestMeshingTag>(_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestCollisionTag>(_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkEndOfPipeTag>(_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainDeferredVisible>(_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestReadbackTag>(_ChunkPrototype, true);

        _SkirtPrototype = mgr.CreateEntity();
        mgr.AddComponent<LocalTransform>(_SkirtPrototype); // 修复：添加 LocalTransform
        mgr.AddComponent<LocalToWorld>(_SkirtPrototype);
        mgr.AddComponent<TerrainSkirt>(_SkirtPrototype);
        mgr.AddComponent<TerrainDeferredVisible>(_SkirtPrototype);
        mgr.AddComponent<TerrainSkirtLinkedParent>(_SkirtPrototype);
        mgr.AddComponent<Prefab>(_SkirtPrototype);
        mgr.SetComponentEnabled<TerrainDeferredVisible>(_SkirtPrototype, false);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_ChunkMap.IsCreated) _ChunkMap.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity)) return;
        var playerPos = SystemAPI.GetComponent<LocalToWorld>(playerEntity).Position;
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var playerChunkPos = VoxelUtils.WorldToChunk(playerPos, config.ChunkSize);

        if (math.all(playerChunkPos == _LastPlayerChunkPos))
        {
            UpdateChunkNeighbours(ref state);
            return;
        }

        _LastPlayerChunkPos = playerChunkPos;
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        var requiredChunks = new NativeHashSet<int3>(1024, Allocator.Temp);
        var spawnSize = config.ChunkSpawnSize;

        for (int x = -spawnSize.x; x <= spawnSize.x; x++)
            for (int y = -spawnSize.y; y <= spawnSize.y; y++)
                for (int z = -spawnSize.x; z <= spawnSize.x; z++)
                {
                    requiredChunks.Add(playerChunkPos + new int3(x, y, z));
                }

        var chunksToDestroy = new NativeList<int3>(Allocator.Temp);
        foreach (var pair in _ChunkMap)
        {
            if (!requiredChunks.Contains(pair.Key))
            {
                chunksToDestroy.Add(pair.Key);
                if (state.EntityManager.Exists(pair.Value))
                    ecb.DestroyEntity(pair.Value);
            }
        }

        foreach (var key in chunksToDestroy)
        {
            _ChunkMap.Remove(key);
        }

        foreach (var pos in requiredChunks)
        {
            if (_ChunkMap.ContainsKey(pos)) continue;

            Entity newChunk = ecb.Instantiate(_ChunkPrototype);
            var skirts = new FixedList64Bytes<Entity>();
            for (byte i = 0; i < 6; i++)
            {
                var skirt = ecb.Instantiate(_SkirtPrototype);
                ecb.SetComponent(skirt, new TerrainSkirt { Direction = i });
                ecb.SetComponent(skirt, new TerrainSkirtLinkedParent { ChunkParent = newChunk });
                ecb.SetComponent(skirt, LocalTransform.FromPosition(pos * config.ChunkSize));
                skirts.Add(skirt);
            }
            ecb.SetComponent(newChunk, new Chunk { Position = pos, Skirts = skirts });
            ecb.SetComponent(newChunk, LocalTransform.FromPosition(pos * config.ChunkSize));
            _ChunkMap.TryAdd(pos, newChunk);
        }

        UpdateChunkNeighbours(ref state);

        requiredChunks.Dispose();
        chunksToDestroy.Dispose();
    }

    [BurstCompile]
    private void UpdateChunkNeighbours(ref SystemState state)
    {
        var job = new UpdateNeighbourMasksJob
        {
            ChunkMap = _ChunkMap.AsReadOnly()
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
                int3 offset = VoxelUtils.To3DIndex(i, new int3(3, 3, 3));
                int3 neighbourPos = chunk.Position + offset - 1;
                if (ChunkMap.ContainsKey(neighbourPos))
                {
                    neighbourMask.SetBits(i, true, 1);
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
                int3 offset = VoxelUtils.To3DIndex(i, new int3(3, 3, 3));
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

public partial struct TerrainReadySystems : IComponentData
{
    public bool ReadbackSystemReady;
    public bool MeshingSystemReady;

    public bool IsReady() => ReadbackSystemReady && MeshingSystemReady;
}