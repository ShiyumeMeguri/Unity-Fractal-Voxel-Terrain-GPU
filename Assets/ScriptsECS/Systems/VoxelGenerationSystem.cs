using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using OptIn.Voxel;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ChunkManagerSystem))]
public partial class VoxelGenerationSystem : SystemBase
{
    private EntityQuery m_NewChunksQuery;
    private const int MaxConcurrentRequests = 16; // 增加并发请求数以加快加载速度

    protected override void OnCreate()
    {
        m_NewChunksQuery = GetEntityQuery(
            ComponentType.ReadOnly<Chunk>(),
            ComponentType.ReadOnly<RequestGpuDataTag>()
        );
        RequireForUpdate(m_NewChunksQuery);
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
    }

    protected override void OnUpdate()
    {
        // Guard Clauses
        if (!SystemAPI.TryGetSingleton(out TerrainConfig config) || !SystemAPI.ManagedAPI.TryGetSingleton(out TerrainResources resources))
            return;

        int activeRequests = SystemAPI.QueryBuilder().WithAll<PendingGpuDataTag>().Build().CalculateEntityCount();
        if (activeRequests >= MaxConcurrentRequests)
            return;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        var newChunkEntities = m_NewChunksQuery.ToEntityArray(Allocator.Temp);
        int slots = MaxConcurrentRequests - activeRequests;
        int chunksToProcess = math.min(newChunkEntities.Length, slots);

        for (int i = 0; i < chunksToProcess; i++)
        {
            Entity entity = newChunkEntities[i];
            var chunk = SystemAPI.GetComponent<Chunk>(entity);
            var paddedChunkSize = config.ChunkSize + 2;
            int numVoxels = paddedChunkSize.x * paddedChunkSize.y * paddedChunkSize.z;

            // 使用池化或创建新的 ComputeBuffer
            var buffer = new ComputeBuffer(numVoxels, UnsafeUtility.SizeOf<Voxel>());

            // 设置并分派 Compute Shader
            int kernel = resources.VoxelComputeShader.FindKernel("CSMain");
            resources.VoxelComputeShader.SetBuffer(kernel, "asyncVoxelBuffer", buffer);
            resources.VoxelComputeShader.SetInts("chunkPosition", chunk.Position.x, chunk.Position.y, chunk.Position.z);
            resources.VoxelComputeShader.SetInts("chunkSize", paddedChunkSize.x, paddedChunkSize.y, paddedChunkSize.z);

            var threadGroupSize = new int3(8, 8, 8);
            var groups = (paddedChunkSize + threadGroupSize - 1) / threadGroupSize;
            resources.VoxelComputeShader.Dispatch(kernel, groups.x, groups.y, groups.z);

            // 请求数据回读
            var tempVoxelData = new NativeArray<Voxel>(numVoxels, Allocator.Persistent);
            var request = AsyncGPUReadback.RequestIntoNativeArray(ref tempVoxelData, buffer);

            // 将请求作为托管组件附加到实体
            ecb.AddComponent(entity, new GpuVoxelDataRequest
            {
                Request = request,
                TempVoxelData = tempVoxelData,
                Buffer = buffer,
            });

            // 更新实体状态
            ecb.SetComponentEnabled<RequestGpuDataTag>(entity, false);
            ecb.SetComponentEnabled<PendingGpuDataTag>(entity, true);
        }

        newChunkEntities.Dispose();
    }
}