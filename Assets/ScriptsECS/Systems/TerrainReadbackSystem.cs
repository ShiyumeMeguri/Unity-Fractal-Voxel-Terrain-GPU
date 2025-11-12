using OptIn.Voxel;
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
        AwaitingReadbackData,
        ProcessingCompletedReadback
    }

    private State m_State;
    private List<Entity> m_Entities;
    private JobHandle m_PendingCopies;
    private ComputeBuffer m_VoxelBuffer;
    private NativeArray<Voxel> m_ReadbackData;
    private NativeArray<int> m_SignCounters;
    private bool m_IsInitialized;
    private AsyncGPUReadbackRequest m_ReadbackRequest;
    private bool m_VoxelsFetched;
    private bool m_CountersFetched;


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
            m_SignCounters = new NativeArray<int>(BATCH_SIZE, Allocator.Persistent);
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
        if (m_SignCounters.IsCreated) m_SignCounters.Dispose();
        m_VoxelBuffer?.Release();
    }

    private void ResetState()
    {
        m_State = State.Idle;
        m_Entities.Clear();
        m_PendingCopies = default;
        m_VoxelsFetched = false;
        m_CountersFetched = false;
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

        // 确保上一个Job链完成
        if (m_State != State.ProcessingCompletedReadback)
        {
            Dependency.Complete();
        }

        switch (m_State)
        {
            case State.Idle:
                TryBeginReadback();
                break;
            case State.AwaitingDispatch:
                m_State = State.AwaitingReadbackData;
                break;
            case State.AwaitingReadbackData:
                TryCheckAndProcessReadback();
                break;
            case State.ProcessingCompletedReadback:
                ProcessCompletedJobs();
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

        m_ReadbackRequest = AsyncGPUReadback.Request(m_VoxelBuffer, OnVoxelDataReady);
    }

    // 回调只负责复制数据和设置标志
    private void OnVoxelDataReady(AsyncGPUReadbackRequest request)
    {
        if (m_State != State.AwaitingReadbackData) return;

        if (request.hasError)
        {
            Debug.LogError("GPU Readback Error.");
            m_VoxelsFetched = true; // 即使出错也要推进状态机
            return;
        }

        request.GetData<Voxel>().CopyTo(m_ReadbackData);
        m_VoxelsFetched = true;
    }

    private void TryCheckAndProcessReadback()
    {
        if (!m_VoxelsFetched) return;

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        var jobHandles = new NativeArray<JobHandle>(m_Entities.Count, Allocator.Temp);

        for (int i = 0; i < m_Entities.Count; i++)
        {
            Entity entity = m_Entities[i];
            if (!SystemAPI.Exists(entity))
            {
                jobHandles[i] = Dependency;
                continue;
            }

            var chunkData = new ChunkVoxelData(config.PaddedChunkSize, Allocator.Persistent);
            EntityManager.AddComponentData(entity, chunkData);

            var slice = m_ReadbackData.GetSubArray(i * voxelsPerChunk, voxelsPerChunk);
            var copyJob = new CopyDataJob { Source = slice, Destination = chunkData.Voxels };
            var copyHandle = copyJob.Schedule(Dependency);

            var counter = new NativeArray<int>(1, Allocator.TempJob);
            var signCounterJob = new CountSignsJob { Voxels = chunkData.Voxels, Counter = counter };
            var signHandle = signCounterJob.Schedule(copyHandle);

            var finalCopyHandle = new FinalizeReadbackJob { CounterSource = counter, CounterDest = m_SignCounters.GetSubArray(i, 1) }.Schedule(signHandle);

            jobHandles[i] = finalCopyHandle;
        }

        m_PendingCopies = JobHandle.CombineDependencies(jobHandles);
        jobHandles.Dispose();

        m_State = State.ProcessingCompletedReadback;
    }

    private void ProcessCompletedJobs()
    {
        if (!m_PendingCopies.IsCompleted) return;
        m_PendingCopies.Complete();

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        for (int i = 0; i < m_Entities.Count; i++)
        {
            Entity entity = m_Entities[i];
            if (!SystemAPI.Exists(entity)) continue;

            bool isEmpty = math.abs(m_SignCounters[i]) == voxelsPerChunk;

            SystemAPI.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(entity, true);

            var skipIfEmpty = SystemAPI.GetComponent<TerrainChunkRequestReadbackTag>(entity).SkipMeshingIfEmpty;

            if (isEmpty && skipIfEmpty)
            {
                SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, false);
                SystemAPI.SetComponentEnabled<TerrainChunkEndOfPipeTag>(entity, true);
                var chunkVoxelData = SystemAPI.GetComponent<ChunkVoxelData>(entity);
                chunkVoxelData.Dispose();
                SystemAPI.SetComponentEnabled<ChunkVoxelData>(entity, false);
            }
            else
            {
                SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, true);
            }
        }

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
        [WriteOnly] public NativeArray<int> Counter;
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
    private struct FinalizeReadbackJob : IJob
    {
        [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> CounterSource;
        [WriteOnly] public NativeSlice<int> CounterDest;
        public void Execute()
        {
            CounterDest[0] = CounterSource[0];
        }
    }
}