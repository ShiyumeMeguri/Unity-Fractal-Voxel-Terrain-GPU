using OptIn.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct VoxelEditSystem : ISystem
{
    private EntityQuery m_ChunkQuery;
    private EntityQuery m_EditRequestQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        m_ChunkQuery = state.GetEntityQuery(typeof(Chunk), typeof(ChunkVoxelData));
        m_EditRequestQuery = state.GetEntityQuery(typeof(VoxelEditRequest));
        state.RequireForUpdate(m_EditRequestQuery);
        state.RequireForUpdate<TerrainConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        var chunkMap = new NativeHashMap<int3, Entity>(m_ChunkQuery.CalculateEntityCount(), Allocator.Temp);
        using (var entities = m_ChunkQuery.ToEntityArray(Allocator.Temp))
        using (var chunks = m_ChunkQuery.ToComponentDataArray<Chunk>(Allocator.Temp))
        {
            for (int i = 0; i < entities.Length; i++)
            {
                chunkMap.TryAdd(chunks[i].Position, entities[i]);
            }
        }

        foreach (var (request, entity) in SystemAPI.Query<RefRO<VoxelEditRequest>>().WithEntityAccess())
        {
            var worldPos = request.ValueRO.WorldPosition;
            var chunkPos = VoxelUtils.WorldToChunk(worldPos, config.ChunkSize); // [修正] 移除 ToVector3Int

            if (chunkMap.TryGetValue(chunkPos, out var chunkEntity))
            {
                if (SystemAPI.HasComponent<ChunkVoxelData>(chunkEntity) && SystemAPI.IsComponentEnabled<ChunkVoxelData>(chunkEntity))
                {
                    var voxelData = SystemAPI.GetComponent<ChunkVoxelData>(chunkEntity);
                    if (voxelData.IsCreated)
                    {
                        var gridPos = (int3)math.floor(worldPos - (float3)(chunkPos * config.ChunkSize));
                        var paddedChunkSize = config.PaddedChunkSize;

                        var voxels = voxelData.Voxels;

                        if (request.ValueRO.Type == VoxelEditRequest.EditType.SetBlock)
                        {
                            var arrayIndex = gridPos + 1;
                            if (VoxelUtils.BoundaryCheck(arrayIndex, paddedChunkSize))
                            {
                                int idx1D = VoxelUtils.To1DIndex(arrayIndex, paddedChunkSize);
                                var voxel = voxels[idx1D];

                                voxel.voxelID = request.ValueRO.VoxelID;
                                if (voxel.voxelID <= 0)
                                {
                                    voxel.Density = voxel.voxelID == 0 ? -1f : 1f;
                                }
                                else
                                {
                                    voxel.Density = 1f; // Blocks are solid
                                }

                                voxels[idx1D] = voxel;

                                ecb.SetComponentEnabled<TerrainChunkRequestMeshingTag>(chunkEntity, true);
                                ecb.SetComponentEnabled<TerrainChunkEndOfPipeTag>(chunkEntity, false);
                            }
                        }
                    }
                }
            }
            ecb.DestroyEntity(entity);
        }

        ecb.Playback(state.EntityManager);
        chunkMap.Dispose();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}