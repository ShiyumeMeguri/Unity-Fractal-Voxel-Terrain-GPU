using System.Collections.Generic;
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
    private Queue<ComputeBuffer> m_BufferPool = new Queue<ComputeBuffer>();
    private List<GpuVoxelDataRequest> m_ActiveRequests = new List<GpuVoxelDataRequest>();
    private const int MaxConcurrentRequests = 8;

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

    protected override void OnDestroy()
    {
        CompleteDependency();
        AsyncGPUReadback.WaitAllRequests();

        foreach (var request in m_ActiveRequests)
        {
            CleanupRequest(request);
        }
        m_ActiveRequests.Clear();

        while (m_BufferPool.Count > 0)
        {
            m_BufferPool.Dequeue()?.Release();
        }
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.TryGetSingleton(out TerrainConfig config) || !SystemAPI.ManagedAPI.TryGetSingleton(out TerrainResources resources))
            return;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        ProcessCompletedRequests(ecb);
        DispatchNewRequests(config, resources, ecb);
    }

    private void ProcessCompletedRequests(EntityCommandBuffer ecb)
    {
        for (int i = m_ActiveRequests.Count - 1; i >= 0; i--)
        {
            var request = m_ActiveRequests[i];
            if (!request.Request.done) continue;

            // [修复] 直接使用 TargetEntity，不再需要循环
            Entity entity = request.TargetEntity;

            if (EntityManager.Exists(entity))
            {
                if (request.Request.hasError)
                {
                    Debug.LogError($"GPU readback error for entity {entity.Index}.");
                }
                else if (request.TempVoxelData.IsCreated)
                {
                    var chunkDataRW = SystemAPI.GetComponentRW<ChunkVoxelData>(entity);

                    if (!chunkDataRW.ValueRO.IsCreated || chunkDataRW.ValueRO.Voxels.Length != request.TempVoxelData.Length)
                    {
                        if (chunkDataRW.ValueRO.IsCreated) chunkDataRW.ValueRO.Voxels.Dispose();
                        chunkDataRW.ValueRW.Voxels = new NativeArray<Voxel>(request.TempVoxelData.Length, Allocator.Persistent);
                    }
                    // [修复] 从 request.TempVoxelData 复制，而不是从一个不存在的 slice
                    chunkDataRW.ValueRW.Voxels.CopyFrom(request.TempVoxelData);

                    ecb.SetComponentEnabled<RequestPaddingUpdateTag>(entity, true);
                }
                ecb.SetComponentEnabled<PendingGpuDataTag>(entity, false);
            }

            CleanupRequest(request);
            m_ActiveRequests.RemoveAt(i);
        }
    }

    private void DispatchNewRequests(TerrainConfig config, TerrainResources resources, EntityCommandBuffer ecb)
    {
        if (m_ActiveRequests.Count >= MaxConcurrentRequests) return;

        using var newChunkEntities = m_NewChunksQuery.ToEntityArray(Allocator.Temp);
        if (newChunkEntities.Length == 0) return;

        int availableSlots = MaxConcurrentRequests - m_ActiveRequests.Count;
        int chunksToProcess = math.min(newChunkEntities.Length, availableSlots);

        var paddedChunkSize = config.ChunkSize + 2;
        int voxelsPerChunk = paddedChunkSize.x * paddedChunkSize.y * paddedChunkSize.z;
        var computeShader = resources.VoxelComputeShader;
        int kernel = computeShader.FindKernel("CSMain");
        var threadGroupSize = new int3(8, 8, 8);
        var groups = (paddedChunkSize + threadGroupSize - 1) / threadGroupSize;

        for (int i = 0; i < chunksToProcess; i++)
        {
            Entity entity = newChunkEntities[i];
            var chunk = SystemAPI.GetComponent<Chunk>(entity);

            if (!m_BufferPool.TryDequeue(out var buffer) || !buffer.IsValid() || buffer.count != voxelsPerChunk)
            {
                buffer?.Release();
                buffer = new ComputeBuffer(voxelsPerChunk, UnsafeUtility.SizeOf<Voxel>());
            }

            computeShader.SetBuffer(kernel, "asyncVoxelBuffer", buffer);
            computeShader.SetInts("chunkPosition", chunk.Position.x, chunk.Position.y, chunk.Position.z);
            computeShader.SetInts("chunkSize", paddedChunkSize.x, paddedChunkSize.y, paddedChunkSize.z);
            computeShader.Dispatch(kernel, groups.x, groups.y, groups.z);

            var tempVoxelData = new NativeArray<Voxel>(voxelsPerChunk, Allocator.Persistent);
            // [修复] 修正 AsyncGPUReadback.RequestIntoNativeArray 的调用
            var request = AsyncGPUReadback.RequestIntoNativeArray(ref tempVoxelData, buffer);

            m_ActiveRequests.Add(new GpuVoxelDataRequest
            {
                Request = request,
                TempVoxelData = tempVoxelData,
                Buffer = buffer,
                // [修复] 将单个实体赋值给 TargetEntity
                TargetEntity = entity
            });

            ecb.SetComponentEnabled<RequestGpuDataTag>(entity, false);
            ecb.SetComponentEnabled<PendingGpuDataTag>(entity, true);
        }
    }

    private void CleanupRequest(GpuVoxelDataRequest request)
    {
        if (request.TempVoxelData.IsCreated) request.TempVoxelData.Dispose();

        if (request.Buffer != null && request.Buffer.IsValid())
        {
            if (m_BufferPool.Count < MaxConcurrentRequests)
                m_BufferPool.Enqueue(request.Buffer);
            else
                request.Buffer.Release();
        }
    }
}