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
    private EntityQuery newChunksQuery;
    private Queue<ComputeBuffer> bufferPool = new Queue<ComputeBuffer>();

    protected override void OnCreate()
    {
        newChunksQuery = GetEntityQuery(
            ComponentType.ReadOnly<Chunk>(),
            ComponentType.ReadOnly<RequestGpuDataTag>()
        );
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
    }

    protected override void OnDestroy()
    {
        // 必须完成任何可能正在访问池化缓冲区的正在进行的作业
        CompleteDependency();

        // 清理在关闭前可能未完成的任何请求
        foreach (var (request, entity) in SystemAPI.Query<GpuVoxelDataRequest>().WithEntityAccess())
        {
            // 如果请求尚未完成，我们需要等待它，以安全地释放资源
            if (!request.Request.done)
            {
                request.Request.WaitForCompletion();
            }
            if (request.TempVoxelData.IsCreated)
            {
                request.TempVoxelData.Dispose();
            }

            // 来自未完成请求的缓冲区不会返回到池中，因此直接释放它
            request.Buffer?.Release();
        }

        // 释放池中所有剩余的缓冲区
        while (bufferPool.Count > 0)
        {
            bufferPool.Dequeue().Release();
        }
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.TryGetSingleton<TerrainConfig>(out var config) || !SystemAPI.ManagedAPI.TryGetSingleton<TerrainResources>(out var resources))
        {
            return;
        }

        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        // 1. 处理已完成的GPU回读请求
        foreach (var (request, entity) in SystemAPI.Query<GpuVoxelDataRequest>().WithEntityAccess().WithAll<PendingGpuDataTag>())
        {
            if (!SystemAPI.Exists(entity) || !request.Request.done) continue;

            var chunkData = SystemAPI.GetComponent<ChunkVoxelData>(entity);

            if (request.Request.hasError)
            {
                Debug.LogError($"GPU readback error for chunk entity {entity.Index}.");
                if (request.TempVoxelData.IsCreated) request.TempVoxelData.Dispose();
            }
            else
            {
                // 如果 NativeArray 尚未创建，则创建它
                if (!chunkData.IsCreated)
                {
                    chunkData.Voxels = new NativeArray<Voxel>(request.TempVoxelData.Length, Allocator.Persistent);
                }

                // 确保缓冲区长度匹配以避免错误
                if (chunkData.Voxels.Length == request.TempVoxelData.Length)
                {
                    chunkData.Voxels.CopyFrom(request.TempVoxelData);
                    ecb.SetComponent(entity, chunkData);
                    ecb.SetComponentEnabled<RequestPaddingUpdateTag>(entity, true);
                }
                else
                {
                    Debug.LogError($"Buffer length mismatch for chunk entity {entity.Index}. Expected {chunkData.Voxels.Length}, got {request.TempVoxelData.Length}.");
                }

                // 临时数据已被复制，现在可以安全地释放
                if (request.TempVoxelData.IsCreated) request.TempVoxelData.Dispose();
            }

            ecb.SetComponentEnabled<PendingGpuDataTag>(entity, false);
            ecb.RemoveComponent<GpuVoxelDataRequest>(entity);

            // 将使用的ComputeBuffer返回到池中以便重用
            if (request.Buffer != null)
            {
                bufferPool.Enqueue(request.Buffer);
            }
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();

        var ecbForDispatch = new EntityCommandBuffer(Allocator.TempJob);

        // 2. 发起新的GPU生成请求
        var newChunkEntities = newChunksQuery.ToEntityArray(Allocator.Temp);

        foreach (var entity in newChunkEntities)
        {
            if (!SystemAPI.IsComponentEnabled<RequestGpuDataTag>(entity)) continue;

            var chunk = SystemAPI.GetComponent<Chunk>(entity);
            var paddedChunkSize = config.ChunkSize + 2;
            int numVoxels = (paddedChunkSize.x) * (paddedChunkSize.y) * (paddedChunkSize.z);

            ComputeBuffer buffer;
            if (bufferPool.Count > 0)
            {
                buffer = bufferPool.Dequeue();
                // 确保缓冲区大小正确（在配置更改时可能不正确）
                if (buffer.count != numVoxels)
                {
                    buffer.Release();
                    buffer = new ComputeBuffer(numVoxels, UnsafeUtility.SizeOf<Voxel>(), ComputeBufferType.Structured);
                }
            }
            else
            {
                buffer = new ComputeBuffer(numVoxels, UnsafeUtility.SizeOf<Voxel>(), ComputeBufferType.Structured);
            }

            int kernel = resources.VoxelComputeShader.FindKernel("CSMain");
            resources.VoxelComputeShader.SetBuffer(kernel, "asyncVoxelBuffer", buffer);
            resources.VoxelComputeShader.SetInts("chunkPosition", chunk.Position.x, chunk.Position.y, chunk.Position.z);
            resources.VoxelComputeShader.SetInts("chunkSize", paddedChunkSize.x, paddedChunkSize.y, paddedChunkSize.z);

            int3 threadGroupSize = new int3(8, 8, 8);
            int3 groups = (paddedChunkSize + threadGroupSize - 1) / threadGroupSize;
            resources.VoxelComputeShader.Dispatch(kernel, groups.x, groups.y, groups.z);

            var tempVoxelData = new NativeArray<Voxel>(numVoxels, Allocator.Persistent);
            var request = AsyncGPUReadback.RequestIntoNativeArray(ref tempVoxelData, buffer);

            ecbForDispatch.AddComponent(entity, new GpuVoxelDataRequest
            {
                Request = request,
                TempVoxelData = tempVoxelData,
                Buffer = buffer
            });

            ecbForDispatch.SetComponentEnabled<RequestGpuDataTag>(entity, false);
            ecbForDispatch.SetComponentEnabled<PendingGpuDataTag>(entity, true);
        }

        newChunkEntities.Dispose();
        ecbForDispatch.Playback(EntityManager);
        ecbForDispatch.Dispose();
    }
}