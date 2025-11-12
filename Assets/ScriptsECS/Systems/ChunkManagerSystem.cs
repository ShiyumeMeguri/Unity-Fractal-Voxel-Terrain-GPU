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
    private int3 m_LastPlayerChunkPos;
    private Entity m_ChunkPrototype;
    private Entity m_SkirtPrototype;
    private NativeHashMap<int3, Entity> m_ChunkMap;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        m_LastPlayerChunkPos = new int3(int.MinValue);
        m_ChunkMap = new NativeHashMap<int3, Entity>(1024, Allocator.Persistent);

        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<TerrainConfig>();

        CreatePrototypes(ref state);

        state.EntityManager.CreateSingleton<TerrainReadySystems>();
    }

    private void CreatePrototypes(ref SystemState state)
    {
        var mgr = state.EntityManager;
        m_ChunkPrototype = mgr.CreateEntity();
        mgr.AddComponent<LocalTransform>(m_ChunkPrototype); // 修复：添加 LocalTransform
        mgr.AddComponent<LocalToWorld>(m_ChunkPrototype);
        mgr.AddComponent<Chunk>(m_ChunkPrototype);
        // Do not add ChunkVoxelData here, it will be added on demand.
        mgr.AddComponent<TerrainChunkRequestReadbackTag>(m_ChunkPrototype);
        mgr.AddComponent<TerrainChunkVoxelsReadyTag>(m_ChunkPrototype);
        mgr.AddComponent<TerrainChunkRequestMeshingTag>(m_ChunkPrototype);
        mgr.AddComponent<TerrainChunkMesh>(m_ChunkPrototype);
        mgr.AddComponent<TerrainChunkRequestCollisionTag>(m_ChunkPrototype);
        mgr.AddComponent<TerrainChunkEndOfPipeTag>(m_ChunkPrototype);
        mgr.AddComponent<TerrainDeferredVisible>(m_ChunkPrototype);
        mgr.AddComponent<Prefab>(m_ChunkPrototype);

        mgr.SetComponentEnabled<TerrainChunkMesh>(m_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(m_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestMeshingTag>(m_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestCollisionTag>(m_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkEndOfPipeTag>(m_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainDeferredVisible>(m_ChunkPrototype, false);
        mgr.SetComponentEnabled<TerrainChunkRequestReadbackTag>(m_ChunkPrototype, true);

        m_SkirtPrototype = mgr.CreateEntity();
        mgr.AddComponent<LocalTransform>(m_SkirtPrototype); // 修复：添加 LocalTransform
        mgr.AddComponent<LocalToWorld>(m_SkirtPrototype);
        mgr.AddComponent<TerrainSkirt>(m_SkirtPrototype);
        mgr.AddComponent<TerrainDeferredVisible>(m_SkirtPrototype);
        mgr.AddComponent<TerrainSkirtLinkedParent>(m_SkirtPrototype);
        mgr.AddComponent<Prefab>(m_SkirtPrototype);
        mgr.SetComponentEnabled<TerrainDeferredVisible>(m_SkirtPrototype, false);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (m_ChunkMap.IsCreated) m_ChunkMap.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<PlayerTag>(out var playerEntity)) return;
        var playerPos = SystemAPI.GetComponent<LocalToWorld>(playerEntity).Position;
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var playerChunkPos = VoxelUtil.WorldToChunk(playerPos, config.ChunkSize);

        if (math.all(playerChunkPos == m_LastPlayerChunkPos))
        {
            UpdateChunkNeighbours(ref state);
            return;
        }

        m_LastPlayerChunkPos = playerChunkPos;
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
        foreach (var pair in m_ChunkMap)
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
            m_ChunkMap.Remove(key);
        }

        foreach (var pos in requiredChunks)
        {
            if (m_ChunkMap.ContainsKey(pos)) continue;

            Entity newChunk = ecb.Instantiate(m_ChunkPrototype);
            var skirts = new FixedList64Bytes<Entity>();
            for (byte i = 0; i < 6; i++)
            {
                var skirt = ecb.Instantiate(m_SkirtPrototype);
                ecb.SetComponent(skirt, new TerrainSkirt { Direction = i });
                ecb.SetComponent(skirt, new TerrainSkirtLinkedParent { ChunkParent = newChunk });
                ecb.SetComponent(skirt, LocalTransform.FromPosition(pos * config.ChunkSize));
                skirts.Add(skirt);
            }
            ecb.SetComponent(newChunk, new Chunk { Position = pos, Skirts = skirts });
            ecb.SetComponent(newChunk, LocalTransform.FromPosition(pos * config.ChunkSize));
            m_ChunkMap.TryAdd(pos, newChunk);
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
            ChunkMap = m_ChunkMap.AsReadOnly()
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
                int3 offset = VoxelUtil.To3DIndex(i, new int3(3, 3, 3));
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
                int3 offset = VoxelUtil.To3DIndex(i, new int3(3, 3, 3));
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