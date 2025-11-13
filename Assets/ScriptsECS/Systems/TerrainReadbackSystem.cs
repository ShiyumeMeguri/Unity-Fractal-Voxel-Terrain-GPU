// Systems/TerrainReadbackSystem.cs
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
    private const int BATCH_SIZE = 8;

    private enum SystemState
    {
        Idle,
        Dispatching,
        AwaitingReadback,
        ProcessingData
    }

    private SystemState _currentState;
    private List<(Entity Entity, bool SkipMeshingIfEmpty)> _batchInfo;
    private JobHandle _processingHandle;

    private ComputeBuffer _voxelBuffer;
    private NativeArray<uint> _readbackData;
    private NativeArray<int> _signCounters;

    private bool _isInitialized;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate<TerrainChunkRequestReadbackTag>();

        _batchInfo = new List<(Entity, bool)>(BATCH_SIZE);
        _currentState = SystemState.Idle;
        _isInitialized = false;
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
        _isInitialized = true;
    }

    protected override void OnDestroy()
    {
        _processingHandle.Complete();
        AsyncGPUReadback.WaitAllRequests();
        if (_readbackData.IsCreated) _readbackData.Dispose();
        if (_signCounters.IsCreated) _signCounters.Dispose();
        _voxelBuffer?.Release();
    }

    protected override void OnUpdate()
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.readback = (_currentState == SystemState.Idle);

        if (_currentState != SystemState.ProcessingData)
        {
            Dependency.Complete();
        }

        switch (_currentState)
        {
            case SystemState.Idle:
                TryBeginReadback();
                break;
            case SystemState.AwaitingReadback:
                // AsyncGPUReadback handles this. We wait for the callback.
                break;
            case SystemState.ProcessingData:
                CompleteProcessing();
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

        _currentState = SystemState.AwaitingReadback;
        _batchInfo.Clear();

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
        if (_currentState != SystemState.AwaitingReadback) return;

        if (request.hasError)
        {
            Debug.LogError("GPU Readback Error.");
            ResetState();
            return;
        }

        request.GetData<uint>().CopyTo(_readbackData);
        _currentState = SystemState.ProcessingData;

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        var handles = new NativeArray<JobHandle>(_batchInfo.Count, Allocator.Temp);

        for (int i = 0; i < _batchInfo.Count; i++)
        {
            var entity = _batchInfo[i].Entity;
            if (!SystemAPI.Exists(entity))
            {
                handles[i] = Dependency;
                continue;
            }

            var voxels = new TerrainChunkVoxels
            {
                Voxels = new NativeArray<VoxelData>(voxelsPerChunk, Allocator.Persistent)
            };
            EntityManager.AddComponentData(entity, voxels);

            var slice = _readbackData.GetSubArray(i * voxelsPerChunk, voxelsPerChunk);
            var unpackJob = new UnpackVoxelDataJob
            {
                Source = slice,
                Destination = voxels.Voxels,
                Counter = _signCounters.GetSubArray(i, 1)
            };
            handles[i] = unpackJob.Schedule(Dependency);

            voxels.AsyncWriteJobHandle = handles[i];
            EntityManager.SetComponentData(entity, voxels);
        }

        _processingHandle = JobHandle.CombineDependencies(handles);
        handles.Dispose();
        // This is a system base, so we need to assign the handle to the system dependency
        this.Dependency = _processingHandle;
    }

    private void CompleteProcessing()
    {
        if (!_processingHandle.IsCompleted) return;
        _processingHandle.Complete();

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

        ResetState();
    }

    private void ResetState()
    {
        _currentState = SystemState.Idle;
        _batchInfo.Clear();
        _processingHandle = default;
    }

    [BurstCompile]
    private struct UnpackVoxelDataJob : IJob
    {
        [ReadOnly] public NativeSlice<uint> Source;
        [WriteOnly] public NativeArray<VoxelData> Destination;
        [WriteOnly] public NativeSlice<int> Counter;

        public void Execute()
        {
            int signSum = 0;
            for (int i = 0; i < Source.Length; i++)
            {
                uint packed = Source[i];
                short voxelID = (short)(packed & 0xFFFF);
                short metadata = (short)(packed >> 16);
                var voxel = new VoxelData { voxelID = voxelID, metadata = metadata };
                Destination[i] = voxel;

                signSum += voxel.IsSolid ? 1 : -1;
            }
            Counter[0] = signSum;
        }
    }
}