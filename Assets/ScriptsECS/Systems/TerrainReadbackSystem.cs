using OptIn.Voxel;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ChunkManagerSystem))]
public partial class TerrainReadbackSystem : SystemBase
{
    private bool m_IsFree;
    private List<Entity> m_Entities;
    private JobHandle? m_PendingCopies;
    private ComputeBuffer m_VoxelBuffer;
    private NativeArray<Voxel> m_ReadbackData;
    private bool m_ReadbackInProgress;
    private bool m_IsInitialized; // 新增初始化标志

    private const int BATCH_SIZE = 8;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate<TerrainChunkRequestReadbackTag>();

        m_Entities = new List<Entity>(BATCH_SIZE);
        m_IsFree = true;
        m_IsInitialized = false; // 初始化为 false
    }

    private void Initialize(TerrainConfig config)
    {
        int numVoxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;
        int totalVoxels = numVoxelsPerChunk * BATCH_SIZE;

        if (totalVoxels > 0)
        {
            m_VoxelBuffer = new ComputeBuffer(totalVoxels, UnsafeUtility.SizeOf<Voxel>(), ComputeBufferType.Structured);
            m_ReadbackData = new NativeArray<Voxel>(totalVoxels, Allocator.Persistent);
        }
        else
        {
            Enabled = false; // 如果配置无效，则禁用系统
        }
    }

    protected override void OnDestroy()
    {
        m_PendingCopies?.Complete();
        AsyncGPUReadback.WaitAllRequests();
        if (m_ReadbackData.IsCreated) m_ReadbackData.Dispose();
        m_VoxelBuffer?.Release();
    }

    private void ResetState()
    {
        m_IsFree = true;
        m_Entities.Clear();
        m_PendingCopies = null;
        m_ReadbackInProgress = false;
    }

    protected override void OnUpdate()
    {
        // 首次更新时执行初始化
        if (!m_IsInitialized)
        {
            var config = SystemAPI.GetSingleton<TerrainConfig>();
            Initialize(config);
            m_IsInitialized = true;
        }

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.ReadbackSystemReady = m_IsFree;

        if (m_IsFree)
        {
            TryBeginReadback();
        }
        else
        {
            TryCheckIfReadbackComplete();
        }
    }

    private void TryBeginReadback()
    {
        var query = GetEntityQuery(ComponentType.ReadOnly<Chunk>(), ComponentType.ReadOnly<TerrainChunkRequestReadbackTag>());
        if (query.IsEmpty) return;

        var resources = SystemAPI.ManagedAPI.GetSingleton<TerrainResources>();
        if (resources.VoxelComputeShader == null) return;

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var paddedChunkSize = config.PaddedChunkSize;
        int voxelsPerChunk = paddedChunkSize.x * paddedChunkSize.y * paddedChunkSize.z;
        if (voxelsPerChunk == 0) return;

        using var entitiesArray = query.ToEntityArray(Allocator.Temp);
        int numToProcess = math.min(BATCH_SIZE, entitiesArray.Length);
        if (numToProcess == 0) return;

        m_IsFree = false;
        m_Entities.Clear();

        var cmd = CommandBufferPool.Get("Voxel Generation Dispatch");
        var compute = resources.VoxelComputeShader;
        int kernel = compute.FindKernel("CSMain");
        var threadGroupSize = new int3(8, 8, 8);
        var groups = (paddedChunkSize + threadGroupSize - 1) / threadGroupSize;

        cmd.SetComputeBufferParam(compute, kernel, "asyncVoxelBuffer", m_VoxelBuffer);

        for (int i = 0; i < numToProcess; i++)
        {
            Entity entity = entitiesArray[i];
            m_Entities.Add(entity);
            var chunk = SystemAPI.GetComponent<Chunk>(entity);

            int baseIndex = i * voxelsPerChunk;
            cmd.SetComputeIntParam(compute, "baseIndex", baseIndex);
            cmd.SetComputeIntParams(compute, "chunkPosition", chunk.Position.x, chunk.Position.y, chunk.Position.z);
            cmd.SetComputeIntParams(compute, "chunkSize", paddedChunkSize.x, paddedChunkSize.y, paddedChunkSize.z);
            cmd.DispatchCompute(compute, kernel, groups.x, groups.y, groups.z);

            EntityManager.SetComponentEnabled<TerrainChunkRequestReadbackTag>(entity, false);
        }

        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        m_ReadbackInProgress = true;
        AsyncGPUReadback.Request(m_VoxelBuffer, OnReadbackComplete);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest request)
    {
        if (m_IsFree || World == null || !World.IsCreated) return;

        if (request.hasError)
        {
            Debug.LogError("GPU Readback Error.");
            ResetState();
            return;
        }

        request.GetData<Voxel>().CopyTo(m_ReadbackData);

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        var jobHandles = new NativeArray<JobHandle>(m_Entities.Count, Allocator.Temp);
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(World.Unmanaged);

        for (int i = 0; i < m_Entities.Count; i++)
        {
            Entity entity = m_Entities[i];
            if (!SystemAPI.Exists(entity)) continue;

            var chunkData = new ChunkVoxelData(config.PaddedChunkSize, Allocator.Persistent);
            ecb.AddComponent(entity, chunkData);
            ecb.SetComponentEnabled<ChunkVoxelData>(entity, true);

            var slice = m_ReadbackData.GetSubArray(i * voxelsPerChunk, voxelsPerChunk);

            var copyJob = new CopyDataJob { Source = slice, Destination = chunkData.Voxels };
            var copyHandle = copyJob.Schedule(Dependency);

            var counter = new NativeArray<int>(1, Allocator.TempJob);
            var signCounterJob = new CountSignsJob { Voxels = chunkData.Voxels, Counter = counter };
            var signHandle = signCounterJob.Schedule(copyHandle);

            var finalJob = new PostReadbackJob
            {
                Entity = entity,
                Counter = counter,
                TotalVoxels = voxelsPerChunk,
                SkipMeshingIfEmpty = SystemAPI.GetComponent<TerrainChunkRequestReadbackTag>(entity).SkipMeshingIfEmpty,
                Ecb = ecb.AsParallelWriter()
            };
            jobHandles[i] = finalJob.Schedule(signHandle);
        }

        m_PendingCopies = JobHandle.CombineDependencies(jobHandles);
        Dependency = JobHandle.CombineDependencies(Dependency, m_PendingCopies.Value);
        jobHandles.Dispose();

        m_ReadbackInProgress = false;
    }

    private void TryCheckIfReadbackComplete()
    {
        if (m_ReadbackInProgress) return;

        if (m_PendingCopies.HasValue && m_PendingCopies.Value.IsCompleted)
        {
            m_PendingCopies.Value.Complete();
            ResetState();
        }
    }

    [BurstCompile]
    private struct CopyDataJob : IJob
    {
        [ReadOnly] public NativeSlice<Voxel> Source;
        [WriteOnly] public NativeArray<Voxel> Destination;
        public void Execute() => Source.CopyTo(Destination);
    }

    [BurstCompile]
    private struct CountSignsJob : IJob
    {
        [ReadOnly] public NativeArray<Voxel> Voxels;
        public NativeArray<int> Counter;
        public void Execute()
        {
            int signSum = 0;
            for (int i = 0; i < Voxels.Length; i++)
            {
                signSum += Voxels[i].Density > 0 ? 1 : -1;
            }
            Counter[0] = signSum;
        }
    }

    [BurstCompile]
    private partial struct PostReadbackJob : IJob
    {
        public Entity Entity;
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> Counter;
        public int TotalVoxels;
        public bool SkipMeshingIfEmpty;
        public EntityCommandBuffer.ParallelWriter Ecb;

        public void Execute()
        {
            bool isEmpty = math.abs(Counter[0]) == TotalVoxels;
            int sortKey = Entity.Index;

            Ecb.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(sortKey, Entity, true);
            if (isEmpty && SkipMeshingIfEmpty)
            {
                Ecb.SetComponentEnabled<TerrainChunkRequestMeshingTag>(sortKey, Entity, false);
                Ecb.SetComponentEnabled<TerrainChunkEndOfPipeTag>(sortKey, Entity, true);
                Ecb.SetComponentEnabled<ChunkVoxelData>(sortKey, Entity, false);
            }
            else
            {
                Ecb.SetComponentEnabled<TerrainChunkRequestMeshingTag>(sortKey, Entity, true);
            }
        }
    }
}