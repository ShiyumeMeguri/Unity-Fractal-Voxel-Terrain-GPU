using OptIn.Voxel;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VoxelGenerationSystem))]
public partial class GpuReadbackSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate(GetEntityQuery(typeof(GpuVoxelDataRequest), typeof(PendingGpuDataTag)));
    }

    protected override void OnUpdate()
    {
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        // 使用 WithoutBurst().Run() 来处理托管组件
        Entities.WithoutBurst().ForEach((Entity entity, GpuVoxelDataRequest request) =>
        {
            // Guard Clause: 如果请求未完成，则跳过
            if (!request.Request.done) return;

            if (request.Request.hasError)
            {
                UnityEngine.Debug.LogError($"GPU readback error for entity {entity.Index}.");
            }
            else
            {
                // 检查实体是否仍然有效并且有 ChunkVoxelData 组件
                if (SystemAPI.HasComponent<ChunkVoxelData>(entity))
                {
                    var chunkDataRW = SystemAPI.GetComponentRW<ChunkVoxelData>(entity);

                    // 确保 NativeArray 已创建且大小正确
                    if (!chunkDataRW.ValueRO.IsCreated || chunkDataRW.ValueRO.Voxels.Length != request.TempVoxelData.Length)
                    {
                        if (chunkDataRW.ValueRO.IsCreated) chunkDataRW.ValueRO.Voxels.Dispose();
                        chunkDataRW.ValueRW.Voxels = new NativeArray<Voxel>(request.TempVoxelData.Length, Allocator.Persistent);
                    }
                    chunkDataRW.ValueRW.Voxels.CopyFrom(request.TempVoxelData);

                    // 转换状态，进入Padding更新阶段
                    ecb.SetComponentEnabled<RequestPaddingUpdateTag>(entity, true);
                }
            }

            // 清理并移除请求组件
            request.Dispose();
            ecb.RemoveComponent<GpuVoxelDataRequest>(entity);
            ecb.SetComponentEnabled<PendingGpuDataTag>(entity, false);
            ecb.SetComponentEnabled<NewChunkTag>(entity, false);

        }).Run();
    }
}