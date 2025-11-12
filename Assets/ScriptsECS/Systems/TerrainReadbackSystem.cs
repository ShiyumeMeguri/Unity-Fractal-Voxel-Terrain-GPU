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

    private State _State;
    private List<(Entity Entity, bool SkipMeshingIfEmpty)> _BatchInfo; // [修复] 存储实体和它的Tag数据
    private JobHandle _PendingCopies;
    private ComputeBuffer _VoxelBuffer;
    private NativeArray<VoxelData> _ReadbackData;
    private NativeArray<int> _SignCounters;
    private bool _IsInitialized;
    private AsyncGPUReadbackRequest _ReadbackRequest;
    private bool _VoxelsFetched;

    private const int BATCH_SIZE = 8;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate<TerrainChunkRequestReadbackTag>();

        _BatchInfo = new List<(Entity, bool)>(BATCH_SIZE);
        _State = State.Idle;
        _IsInitialized = false;
    }

    private void Initialize(TerrainConfig config)
    {
        int numVoxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;
        int totalVoxels = numVoxelsPerChunk * BATCH_SIZE;

        if (totalVoxels > 0)
        {
            _VoxelBuffer = new ComputeBuffer(totalVoxels, UnsafeUtility.SizeOf<VoxelData>(), ComputeBufferType.Structured);
            _ReadbackData = new NativeArray<VoxelData>(totalVoxels, Allocator.Persistent);
            _SignCounters = new NativeArray<int>(BATCH_SIZE, Allocator.Persistent);
        }
        else
        {
            Enabled = false;
        }
        _IsInitialized = true;
    }

    protected override void OnDestroy()
    {
        _PendingCopies.Complete();
        AsyncGPUReadback.WaitAllRequests();
        if (_ReadbackData.IsCreated) _ReadbackData.Dispose();
        if (_SignCounters.IsCreated) _SignCounters.Dispose();
        _VoxelBuffer?.Release();
    }

    private void ResetState()
    {
        _State = State.Idle;
        _BatchInfo.Clear();
        _PendingCopies = default;
        _VoxelsFetched = false;
    }

    protected override void OnUpdate()
    {
        if (!_IsInitialized)
        {
            var config = SystemAPI.GetSingleton<TerrainConfig>();
            Initialize(config);
        }

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.ReadbackSystemReady = (_State == State.Idle);

        if (_State != State.ProcessingCompletedReadback)
        {
            Dependency.Complete();
        }

        switch (_State)
        {
            case State.Idle:
                TryBeginReadback();
                break;
            case State.AwaitingDispatch:
                _State = State.AwaitingReadbackData;
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

        _State = State.AwaitingDispatch;
        _BatchInfo.Clear();

        var cmd = CommandBufferPool.Get("Voxel Generation Dispatch");
        var compute = resources.VoxelComputeShader;
        int kernel = compute.FindKernel("CSMain");
        if (kernel == -1)
        {
            Debug.LogError("VoxelCompute.compute: Kernel CSMain is invalid. Check for shader compilation errors.");
            CommandBufferPool.Release(cmd);
            _State = State.Idle;
            return;
        }

        var threadGroupSize = new int3(8, 8, 8);
        var groups = (paddedChunkSize + threadGroupSize - 1) / threadGroupSize;

        cmd.SetComputeBufferParam(compute, kernel, "asyncVoxelBuffer", _VoxelBuffer);

        for (int i = 0; i < numToProcess; i++)
        {
            Entity entity = entitiesArray[i];

            // [修复] 在禁用Tag之前读取并缓存数据
            var requestTag = SystemAPI.GetComponent<TerrainChunkRequestReadbackTag>(entity);
            _BatchInfo.Add((entity, requestTag.SkipMeshingIfEmpty));

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

        _ReadbackRequest = AsyncGPUReadback.Request(_VoxelBuffer, OnVoxelDataReady);
    }

    private void OnVoxelDataReady(AsyncGPUReadbackRequest request)
    {
        if (_State != State.AwaitingReadbackData) return;

        if (request.hasError)
        {
            Debug.LogError("GPU Readback Error.");
            _VoxelsFetched = true;
            return;
        }

        request.GetData<VoxelData>().CopyTo(_ReadbackData);
        _VoxelsFetched = true;
    }

    private void TryCheckAndProcessReadback()
    {
        if (!_VoxelsFetched) return;

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        JobHandle combinedHandle = Dependency;

        for (int i = 0; i < _BatchInfo.Count; i++)
        {
            Entity entity = _BatchInfo[i].Entity;
            if (!SystemAPI.Exists(entity))
            {
                continue;
            }

            var chunkData = new TerrainChunkVoxels(config.PaddedChunkSize, Allocator.Persistent);
            ecb.AddComponent(entity, chunkData);

            var slice = _ReadbackData.GetSubArray(i * voxelsPerChunk, voxelsPerChunk);
            var copyJob = new CopyDataJob { Source = slice, Destination = chunkData.Voxels };
            var copyHandle = copyJob.Schedule(combinedHandle);

            var counter = new NativeArray<int>(1, Allocator.TempJob);
            var signCounterJob = new CountSignsJob { Voxels = chunkData.Voxels, Counter = counter };
            var signHandle = signCounterJob.Schedule(copyHandle);

            var finalJob = new FinalizeReadbackJob { CounterSource = counter, CounterDest = _SignCounters.GetSubArray(i, 1) };
            var finalHandle = finalJob.Schedule(signHandle);

            combinedHandle = finalHandle;
        }

        _PendingCopies = combinedHandle;
        _State = State.ProcessingCompletedReadback;
    }

    private void ProcessCompletedJobs()
    {
        if (!_PendingCopies.IsCompleted) return;
        _PendingCopies.Complete();

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        for (int i = 0; i < _BatchInfo.Count; i++)
        {
            Entity entity = _BatchInfo[i].Entity;
            if (!SystemAPI.Exists(entity)) continue;

            bool isEmpty = math.abs(_SignCounters[i]) == voxelsPerChunk;
            bool skipIfEmpty = _BatchInfo[i].SkipMeshingIfEmpty; // [修复] 使用缓存的数据

            SystemAPI.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(entity, true);

            if (isEmpty && skipIfEmpty)
            {
                SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, false);
                SystemAPI.SetComponentEnabled<TerrainChunkEndOfPipeTag>(entity, true);
                if (SystemAPI.HasComponent<TerrainChunkVoxels>(entity))
                {
                    var chunkVoxelData = SystemAPI.GetComponent<TerrainChunkVoxels>(entity);
                    chunkVoxelData.Dispose();
                    SystemAPI.SetComponentEnabled<TerrainChunkVoxels>(entity, false);
                }
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
        [ReadOnly] public NativeSlice<VoxelData> Source;
        [WriteOnly] public NativeArray<VoxelData> Destination;
        public void Execute() => Source.CopyTo(Destination);
    }

    [BurstCompile]
    private struct CountSignsJob : IJob
    {
        [ReadOnly] public NativeArray<VoxelData> Voxels;
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