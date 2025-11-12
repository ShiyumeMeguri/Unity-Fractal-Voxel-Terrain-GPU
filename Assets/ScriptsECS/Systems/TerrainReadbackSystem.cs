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
    private enum State
    {
        Idle,
        AwaitingDispatch,
        AwaitingReadback
    }

    private State m_State;
    private List<Entity> m_Entities;
    private JobHandle m_PendingCopies;
    private ComputeBuffer m_VoxelBuffer;
    private NativeArray<Voxel> m_ReadbackData;
    private bool m_IsInitialized;
    private AsyncGPUReadbackRequest m_ReadbackRequest;

    private const int BATCH_SIZE = 8;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate<TerrainChunkRequestReadbackTag>();

        m_Entities = new List<Entity>(BATCH_SIZE);
        m_State = State.Idle;
        m_IsInitialized = false;
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
            Enabled = false;
        }
        m_IsInitialized = true;
    }

    protected override void OnDestroy()
    {
        m_PendingCopies.Complete();
        AsyncGPUReadback.WaitAllRequests();
        if (m_ReadbackData.IsCreated) m_ReadbackData.Dispose();
        m_VoxelBuffer?.Release();
    }

    // [修复] 重新添加 ResetState 方法
    private void ResetState()
    {
        m_State = State.Idle;
        m_Entities.Clear();
        m_PendingCopies = default;
    }


    protected override void OnUpdate()
    {
        if (!m_IsInitialized)
        {
            var config = SystemAPI.GetSingleton<TerrainConfig>();
            Initialize(config);
        }

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.ReadbackSystemReady = (m_State == State.Idle);

        switch (m_State)
        {
            case State.Idle:
                TryBeginReadback();
                break;
            case State.AwaitingDispatch:
                m_State = State.AwaitingReadback;
                break;
            case State.AwaitingReadback:
                TryCheckAndProcessReadback();
                break;
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

        m_State = State.AwaitingDispatch;
        m_Entities.Clear();

        var cmd = CommandBufferPool.Get("Voxel Generation Dispatch");
        var compute = resources.VoxelComputeShader;
        int kernel = compute.FindKernel("CSMain");
        if (kernel == -1)
        {
            Debug.LogError("VoxelCompute.compute: Kernel CSMain is invalid. Check for shader compilation errors.");
            CommandBufferPool.Release(cmd);
            m_State = State.Idle;
            return;
        }

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

        m_ReadbackRequest = AsyncGPUReadback.Request(m_VoxelBuffer);
    }

    private void TryCheckAndProcessReadback()
    {
        if (!m_ReadbackRequest.done) return;

        if (m_ReadbackRequest.hasError)
        {
            Debug.LogError("GPU Readback Error.");
            ResetState();
            return;
        }

        m_ReadbackRequest.GetData<Voxel>().CopyTo(m_ReadbackData);

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        var jobHandles = new NativeArray<JobHandle>(m_Entities.Count, Allocator.Temp);

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
        Dependency = m_PendingCopies;
        jobHandles.Dispose();

        ResetState();
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