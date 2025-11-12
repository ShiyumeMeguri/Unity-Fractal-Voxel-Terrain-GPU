using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using OptIn.Voxel;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct VoxelEditSystem : ISystem
{
    private EntityQuery chunkQuery;
    private EntityQuery editRequestQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // [修正] 查询现在使用 ChunkVoxelData
        chunkQuery = state.GetEntityQuery(typeof(Chunk), typeof(ChunkVoxelData));
        editRequestQuery = state.GetEntityQuery(typeof(VoxelEditRequest));
        state.RequireForUpdate(editRequestQuery);
        state.RequireForUpdate<TerrainConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        var chunkMap = new NativeHashMap<int3, Entity>(chunkQuery.CalculateEntityCount(), Allocator.Temp);
        using (var entities = chunkQuery.ToEntityArray(Allocator.Temp))
        using (var chunks = chunkQuery.ToComponentDataArray<Chunk>(Allocator.Temp))
        {
            for (int i = 0; i < entities.Length; i++)
            {
                chunkMap.TryAdd(chunks[i].Position, entities[i]);
            }
        }

        foreach (var (request, entity) in SystemAPI.Query<RefRO<VoxelEditRequest>>().WithEntityAccess())
        {
            var worldPos = request.ValueRO.Center;
            var chunkPos = (int3)math.floor(worldPos / (float3)config.ChunkSize);

            if (chunkMap.TryGetValue(chunkPos, out var chunkEntity))
            {
                // [修正] 检查并获取 ChunkVoxelData 组件
                if (SystemAPI.HasComponent<ChunkVoxelData>(chunkEntity))
                {
                    var voxelData = SystemAPI.GetComponent<ChunkVoxelData>(chunkEntity);
                    if (voxelData.IsCreated)
                    {
                        var gridPos = (int3)math.floor(worldPos - chunkPos * config.ChunkSize);
                        var paddedChunkSize = config.ChunkSize + 2;
                        
                        // [修正] 直接操作从 ChunkVoxelData 获取的 NativeArray
                        var voxels = voxelData.Voxels;

                        if (request.ValueRO.Type == VoxelEditRequest.EditType.SetBlock)
                        {
                            var arrayIndex = gridPos + 1;
                            if (VoxelUtil.BoundaryCheck(arrayIndex, paddedChunkSize))
                            {
                                int idx1D = VoxelUtil.To1DIndex(arrayIndex, paddedChunkSize);
                                var voxel = voxels[idx1D];
                                
                                voxel.voxelID = request.ValueRO.VoxelID;
                                if (voxel.voxelID <= 0)
                                {
                                    voxel.Density = voxel.voxelID == 0 ? -1f : 1f;
                                }
                                
                                voxels[idx1D] = voxel; // 写回数组
                            }
                        }
                        
                        ecb.SetComponentEnabled<ChunkModifiedTag>(chunkEntity, true);
                        ecb.SetComponentEnabled<RequestMeshTag>(chunkEntity, true);
                        ecb.SetComponentEnabled<IdleTag>(chunkEntity, false);
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