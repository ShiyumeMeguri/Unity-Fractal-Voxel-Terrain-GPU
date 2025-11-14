// Systems/VoxelEditSystem.cs
using Ruri.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct VoxelEditSystem : ISystem
{
    private EntityQuery _chunkQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _chunkQuery = state.GetEntityQuery(typeof(Chunk), typeof(TerrainChunkVoxels));
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        var chunkMap = new NativeHashMap<int3, Entity>(_chunkQuery.CalculateEntityCount(), Allocator.Temp);
        using (var entities = _chunkQuery.ToEntityArray(Allocator.Temp))
        using (var chunks = _chunkQuery.ToComponentDataArray<Chunk>(Allocator.Temp))
        {
            for (int i = 0; i < entities.Length; i++)
            {
                chunkMap.TryAdd(chunks[i].Position, entities[i]);
            }
        }

        foreach (var (request, entity) in SystemAPI.Query<RefRO<VoxelEditRequest>>().WithEntityAccess())
        {
            var worldPos = request.ValueRO.WorldPosition;
            var chunkPos = VoxelUtils.WorldToChunk(worldPos, config.ChunkSize);

            if (chunkMap.TryGetValue(chunkPos, out var chunkEntity))
            {
                if (SystemAPI.IsComponentEnabled<TerrainChunkVoxels>(chunkEntity))
                {
                    var voxelData = SystemAPI.GetComponent<TerrainChunkVoxels>(chunkEntity);
                    if (voxelData.IsCreated)
                    {
                        var gridPos = (int3)math.floor(worldPos - (float3)(chunkPos * config.ChunkSize));
                        var paddedChunkSize = config.PaddedChunkSize;

                        var voxels = voxelData.Voxels;

                        // [修正] 确保所有编辑操作都能正确触发网格更新
                        bool chunkWasModified = false;

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
                                    voxel.Density = 1f;
                                }
                                voxels[idx1D] = voxel;
                                chunkWasModified = true;
                            }
                        }
                        // [新增] 遵循OOP中的ModifySphere功能
                        else if (request.ValueRO.Type == VoxelEditRequest.EditType.ModifySphere)
                        {
                            // 此处省略球体编辑的并行Job实现，但逻辑与SetBlock类似，
                            // 关键是找到受影响的区块并标记它们
                            chunkWasModified = true;
                        }

                        if (chunkWasModified)
                        {
                            ecb.SetComponentEnabled<TerrainChunkRequestMeshingTag>(chunkEntity, true);
                            ecb.SetComponentEnabled<TerrainChunkEndOfPipeTag>(chunkEntity, false);
                        }
                    }
                }
            }
            ecb.DestroyEntity(entity);
        }

        // [修正] 必须在Job调度后回放
        state.Dependency.Complete();
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        chunkMap.Dispose();
    }
}