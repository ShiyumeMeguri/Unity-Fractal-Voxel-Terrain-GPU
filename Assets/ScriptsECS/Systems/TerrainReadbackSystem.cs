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

// 1. 定义一个Tag，用于标记正在等待数据解包的实体批次
public struct AwaitingVoxelDataUnpackTag : IComponentData { }

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ChunkManagerSystem))]
public partial class TerrainReadbackSystem : SystemBase
{
    private const int BATCH_SIZE = 8;

    private enum SystemState
    {
        Idle,
        DispatchingAndAwaitingReadback
    }

    private SystemState _currentState;
    private JobHandle _processingHandle;

    private ComputeBuffer _voxelBuffer;
    private NativeArray<uint> _readbackData;
    private NativeArray<int> _signCounters;

    private bool _isInitialized;
    private volatile bool _dataIsReady;

    // 存储当前批次的信息，以便在下一帧的Job中使用
    private NativeHashMap<Entity, int> _entityToIndexMap;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate<TerrainChunkRequestReadbackTag>();

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
            _entityToIndexMap = new NativeHashMap<Entity, int>(BATCH_SIZE, Allocator.Persistent);
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
            if (_entityToIndexMap.IsCreated) _entityToIndexMap.Dispose();
            _voxelBuffer?.Release();
        }
    }

    protected override void OnUpdate()
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        // 必须先完成上一帧的所有处理
        _processingHandle.Complete();

        // 2. 应用上一帧处理的结果
        ApplyProcessingResults();

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.readback = (_currentState == SystemState.Idle);

        if (_currentState == SystemState.Idle)
        {
            // 3. 尝试开始新的读回
            TryBeginReadback();
        }
        else if (_currentState == SystemState.DispatchingAndAwaitingReadback && _dataIsReady)
        {
            // 4. GPU数据已返回，调度本帧的CPU处理作业
            ScheduleProcessingJobs();
            _currentState = SystemState.Idle;
            _dataIsReady = false;
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
        _entityToIndexMap.Clear();

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
            _entityToIndexMap.Add(entity, i);

            EntityManager.AddComponent<AwaitingVoxelDataUnpackTag>(entity);
            EntityManager.SetComponentEnabled<TerrainChunkRequestReadbackTag>(entity, false);

            var chunk = SystemAPI.GetComponent<Chunk>(entity);
            cmd.SetComputeIntParam(compute, "baseIndex", i * voxelsPerChunk);
            cmd.SetComputeIntParams(compute, "chunkPosition", chunk.Position.x, chunk.Position.y, chunk.Position.z);
            cmd.SetComputeIntParams(compute, "chunkSize", paddedChunkSize.x, paddedChunkSize.y, paddedChunkSize.z);
            cmd.DispatchCompute(compute, kernel, groups.x, groups.y, groups.z);
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
            _dataIsReady = true;
            return;
        }

        request.GetData<uint>().CopyTo(_readbackData);
        _dataIsReady = true;
    }

    private void ScheduleProcessingJobs()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();

        var unpackJob = new UnpackVoxelDataJobChunk
        {
            VoxelDataTypeHandle = GetComponentTypeHandle<TerrainChunkVoxels>(false),
            EntityTypeHandle = GetEntityTypeHandle(),
            EntityToIndexMap = _entityToIndexMap.AsReadOnly(),
            SourceData = _readbackData,
            SignCounters = _signCounters,
            VoxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z,
        };

        var query = GetEntityQuery(ComponentType.ReadWrite<TerrainChunkVoxels>(), ComponentType.ReadOnly<AwaitingVoxelDataUnpackTag>());
        _processingHandle = unpackJob.ScheduleParallel(query, Dependency);
        Dependency = _processingHandle;
    }

    private void ApplyProcessingResults()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 使用 _entityToIndexMap 来查找正确的批次索引和实体
        foreach (var pair in _entityToIndexMap)
        {
            Entity entity = pair.Key;
            int batchIndex = pair.Value;

            if (!SystemAPI.Exists(entity)) continue;

            var requestTag = SystemAPI.GetComponent<TerrainChunkRequestReadbackTag>(entity); // 之前被禁用了，但数据还在
            bool isEmpty = math.abs(_signCounters[batchIndex]) == voxelsPerChunk;

            ecb.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(entity, true);

            if (isEmpty && requestTag.SkipMeshingIfEmpty)
            {
                ecb.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, false);
                ecb.SetComponentEnabled<TerrainChunkEndOfPipeTag>(entity, true);
            }
            else
            {
                ecb.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, true);
            }

            // 移除Tag，表示处理完成
            ecb.RemoveComponent<AwaitingVoxelDataUnpackTag>(entity);
        }

        ecb.Playback(EntityManager);
    }

    private void ResetState()
    {
        _currentState = SystemState.Idle;
    }

    [BurstCompile]
    private struct UnpackVoxelDataJobChunk : IJobChunk
    {
        public ComponentTypeHandle<TerrainChunkVoxels> VoxelDataTypeHandle;
        [ReadOnly] public EntityTypeHandle EntityTypeHandle;

        [ReadOnly] public NativeHashMap<Entity, int>.ReadOnly EntityToIndexMap;
        [ReadOnly] public NativeArray<uint> SourceData;

        [WriteOnly]
        [NativeDisableParallelForRestriction] // 安全：每个并行的 Execute 调用会写入到不同的索引
        public NativeArray<int> SignCounters;

        public int VoxelsPerChunk;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityTypeHandle);
            var chunkVoxelsArray = chunk.GetNativeArray(ref VoxelDataTypeHandle);

            for (int i = 0; i < chunk.Count; i++)
            {
                var entity = entities[i];
                var chunkVoxels = chunkVoxelsArray[i];
                var batchIndex = EntityToIndexMap[entity];

                var sourceSlice = SourceData.GetSubArray(batchIndex * VoxelsPerChunk, VoxelsPerChunk);
                var destination = chunkVoxels.Voxels;

                int signSum = 0;
                for (int j = 0; j < sourceSlice.Length; j++)
                {
                    uint packed = sourceSlice[j];
                    short voxelID = (short)(packed & 0xFFFF);
                    short metadata = (short)(packed >> 16);
                    var voxel = new VoxelData { voxelID = voxelID, metadata = metadata };
                    destination[j] = voxel;

                    signSum += voxel.IsSolid ? 1 : -1;
                }
                SignCounters[batchIndex] = signSum;
            }
        }
    }
}