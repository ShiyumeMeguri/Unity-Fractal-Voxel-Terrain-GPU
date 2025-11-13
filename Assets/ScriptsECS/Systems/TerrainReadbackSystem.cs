// Systems/TerrainReadbackSystem.cs
using OptIn.Voxel;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ChunkManagerSystem))]
public partial class TerrainReadbackSystem : SystemBase
{
    private const int BATCH_SIZE = 8;

    private enum SystemState
    {
        Idle,
        DispatchingAndAwaitingReadback,
        ProcessingData
    }

    private SystemState _currentState;
    private List<(Entity Entity, bool SkipMeshingIfEmpty)> _batchInfo;
    private JobHandle _processingHandle;

    private ComputeBuffer _voxelBuffer;
    private NativeArray<uint> _readbackData;
    private NativeArray<int> _signCounters;

    private bool _isInitialized;
    private volatile bool _dataIsReady;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate<TerrainChunkRequestReadbackTag>();

        _batchInfo = new List<(Entity, bool)>(BATCH_SIZE);
        _currentState = SystemState.Idle;
        _isInitialized = false;
        _processingHandle = default;
        _dataIsReady = false;
    }

    private void Initialize()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int numVoxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;
        int totalVoxels = numVoxelsPerChunk * BATCH_SIZE;

        if (totalVoxels > 0)
        {
            _voxelBuffer = new ComputeBuffer(totalVoxels, sizeof(uint), ComputeBufferType.Structured);
            _readbackData = new NativeArray<uint>(totalVoxels, Allocator.Persistent);
            _signCounters = new NativeArray<int>(BATCH_SIZE, Allocator.Persistent);
        }
        else
        {
            Enabled = false;
        }
        _isInitialized = true;
    }

    protected override void OnDestroy()
    {
        _processingHandle.Complete();
        AsyncGPUReadback.WaitAllRequests();

        if (_isInitialized)
        {
            if (_readbackData.IsCreated) _readbackData.Dispose();
            if (_signCounters.IsCreated) _signCounters.Dispose();
            _voxelBuffer?.Release();
        }
    }

    protected override void OnUpdate()
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.readback = (_currentState == SystemState.Idle && _processingHandle.IsCompleted);

        // 必须在状态切换前完成上一帧的依赖
        Dependency.Complete();

        switch (_currentState)
        {
            case SystemState.Idle:
                if (_processingHandle.IsCompleted)
                {
                    TryBeginReadback();
                }
                break;

            case SystemState.DispatchingAndAwaitingReadback:
                if (_dataIsReady)
                {
                    ScheduleProcessingJobs();
                    _currentState = SystemState.ProcessingData;
                }
                break;

            case SystemState.ProcessingData:
                if (_processingHandle.IsCompleted)
                {
                    ApplyProcessingResults();
                    ResetState();
                }
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

        using var entities = query.ToEntityArray(Allocator.Temp);
        int numToProcess = math.min(BATCH_SIZE, entities.Length);
        if (numToProcess == 0) return;

        _currentState = SystemState.DispatchingAndAwaitingReadback;
        _batchInfo.Clear();
        _dataIsReady = false;

        var cmd = CommandBufferPool.Get("Voxel Generation");
        var compute = resources.VoxelComputeShader;
        int kernel = compute.FindKernel("CSMain");

        uint tx, ty, tz;
        compute.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);
        var groups = (paddedChunkSize + new int3((int)tx - 1, (int)ty - 1, (int)tz - 1)) / new int3((int)tx, (int)ty, (int)tz);

        cmd.SetComputeBufferParam(compute, kernel, "asyncVoxelBuffer", _voxelBuffer);

        for (int i = 0; i < numToProcess; i++)
        {
            Entity entity = entities[i];
            var requestTag = SystemAPI.GetComponent<TerrainChunkRequestReadbackTag>(entity);
            _batchInfo.Add((entity, requestTag.SkipMeshingIfEmpty));
            var chunk = SystemAPI.GetComponent<Chunk>(entity);

            cmd.SetComputeIntParam(compute, "baseIndex", i * voxelsPerChunk);
            cmd.SetComputeIntParams(compute, "chunkPosition", chunk.Position.x, chunk.Position.y, chunk.Position.z);
            cmd.SetComputeIntParams(compute, "chunkSize", paddedChunkSize.x, paddedChunkSize.y, paddedChunkSize.z);
            cmd.DispatchCompute(compute, kernel, groups.x, groups.y, groups.z);
            EntityManager.SetComponentEnabled<TerrainChunkRequestReadbackTag>(entity, false);
        }

        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        AsyncGPUReadback.Request(_voxelBuffer, OnVoxelDataReady);
    }

    private void OnVoxelDataReady(AsyncGPUReadbackRequest request)
    {
        if (_currentState != SystemState.DispatchingAndAwaitingReadback) return;

        if (request.hasError)
        {
            Debug.LogError("GPU Readback Error.");
            _dataIsReady = true; // 即使出错也要设置标志，以便系统可以重置
            return;
        }

        request.GetData<uint>().CopyTo(_readbackData);
        _dataIsReady = true;
    }

    private void ScheduleProcessingJobs()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        var entitiesToProcess = new NativeArray<Entity>(_batchInfo.Count, Allocator.TempJob);
        for (int i = 0; i < _batchInfo.Count; ++i)
        {
            Entity entity = _batchInfo[i].Entity;
            if (SystemAPI.Exists(entity))
            {
                var voxels = new TerrainChunkVoxels
                {
                    Voxels = new NativeArray<VoxelData>(voxelsPerChunk, Allocator.Persistent)
                };
                EntityManager.AddComponentData(entity, voxels);
                entitiesToProcess[i] = entity;
            }
            else
            {
                entitiesToProcess[i] = Entity.Null;
            }
        }

        var unpackJob = new UnpackVoxelDataBatchJob
        {
            Entities = entitiesToProcess,
            ChunkVoxelsLookup = GetComponentLookup<TerrainChunkVoxels>(false),
            Source = _readbackData,
            Counters = _signCounters,
            VoxelsPerChunk = voxelsPerChunk,
        };

        // 确保新的 Job 链等待上一个 Job 链完成
        _processingHandle = unpackJob.Schedule(_batchInfo.Count, 1, JobHandle.CombineDependencies(Dependency, _processingHandle));

        // 在主线程上为所有新创建的组件设置 JobHandle
        for (int i = 0; i < _batchInfo.Count; i++)
        {
            Entity entity = entitiesToProcess[i];
            if (entity != Entity.Null)
            {
                var voxels = EntityManager.GetComponentData<TerrainChunkVoxels>(entity);
                voxels.AsyncWriteJobHandle = _processingHandle;
                EntityManager.SetComponentData(entity, voxels);
            }
        }

        Dependency = _processingHandle;
    }

    private void ApplyProcessingResults()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        for (int i = 0; i < _batchInfo.Count; i++)
        {
            Entity entity = _batchInfo[i].Entity;
            if (!SystemAPI.Exists(entity)) continue;

            bool isEmpty = math.abs(_signCounters[i]) == voxelsPerChunk;
            bool skipIfEmpty = _batchInfo[i].SkipMeshingIfEmpty;

            SystemAPI.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(entity, true);

            if (isEmpty && skipIfEmpty)
            {
                SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, false);
                SystemAPI.SetComponentEnabled<TerrainChunkEndOfPipeTag>(entity, true);
            }
            else
            {
                SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, true);
            }
        }
    }

    private void ResetState()
    {
        _currentState = SystemState.Idle;
        _batchInfo.Clear();
    }

    [BurstCompile]
    private struct UnpackVoxelDataBatchJob : IJobParallelFor
    {
        [DeallocateOnJobCompletion][ReadOnly] public NativeArray<Entity> Entities;
        [NativeDisableParallelForRestriction] public ComponentLookup<TerrainChunkVoxels> ChunkVoxelsLookup;

        [ReadOnly] public NativeArray<uint> Source;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> Counters;

        public int VoxelsPerChunk;

        public void Execute(int i)
        {
            Entity entity = Entities[i];
            if (entity == Entity.Null) return;

            var chunkVoxels = ChunkVoxelsLookup[entity];
            var sourceSlice = Source.GetSubArray(i * VoxelsPerChunk, VoxelsPerChunk);

            int signSum = 0;
            for (int j = 0; j < sourceSlice.Length; j++)
            {
                uint packed = sourceSlice[j];
                short voxelID = (short)(packed & 0xFFFF);
                short metadata = (short)(packed >> 16);
                var voxel = new VoxelData { voxelID = voxelID, metadata = metadata };
                chunkVoxels.Voxels[j] = voxel;

                signSum += voxel.IsSolid ? 1 : -1;
            }
            Counters[i] = signSum;
        }
    }
}