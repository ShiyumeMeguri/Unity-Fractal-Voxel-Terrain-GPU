// Systems/TerrainReadbackSystem.cs
using Ruri.Voxel;
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
    private const int BATCH_SIZE = 64;

    private bool _free;
    private bool _disposed;
    private bool _dataFetched;
    private List<Entity> _batchEntities;
    private ComputeBuffer _voxelBuffer;
    private NativeArray<VoxelData> _readbackData;
    private JobHandle? _pendingCopies;
    private NativeArray<JobHandle> _copyHandles;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate<TerrainReadbackConfig>();

        _free = true;
        _disposed = false;
        _dataFetched = false;
        _batchEntities = new List<Entity>(BATCH_SIZE);
        _copyHandles = new NativeArray<JobHandle>(BATCH_SIZE, Allocator.Persistent);

        Debug.Log("[TerrainReadbackSystem] OnCreate: System created.");
    }

    protected override void OnDestroy()
    {
        _disposed = true;
        _pendingCopies?.Complete();
        AsyncGPUReadback.WaitAllRequests();
        if (_copyHandles.IsCreated) _copyHandles.Dispose();
        if (_readbackData.IsCreated) _readbackData.Dispose();
        _voxelBuffer?.Release();
        Debug.Log("[TerrainReadbackSystem] OnDestroy: System destroyed and resources released.");
    }

    protected override void OnUpdate()
    {
        var query = GetEntityQuery(ComponentType.ReadOnly<Chunk>(), ComponentType.ReadOnly<TerrainChunkRequestReadbackTag>());
        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.readback = query.IsEmpty && _free;

        if (UnityEngine.Time.frameCount % 120 == 0)
        {
            Debug.Log($"[TerrainReadbackSystem] OnUpdate: Free = {_free}, Chunks to Readback = {query.CalculateEntityCount()}, Ready Flag = {readySystems.readback}");
        }

        if (_free)
        {
            TryBeginReadback(query);
        }
        else if (_dataFetched && _pendingCopies.HasValue)
        {
            if (_pendingCopies.Value.IsCompleted)
            {
                _pendingCopies.Value.Complete();
                FinalizeBatch();
            }
        }
    }

    private void TryBeginReadback(EntityQuery query)
    {
        if (query.IsEmpty) return;

        var resources = SystemAPI.ManagedAPI.GetSingleton<TerrainResources>();
        if (resources.VoxelComputeShader == null) return;

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var paddedChunkSize = config.PaddedChunkSize;
        int voxelsPerChunk = paddedChunkSize.x * paddedChunkSize.y * paddedChunkSize.z;
        int totalVoxelsInBatch = voxelsPerChunk * BATCH_SIZE;

        if (_voxelBuffer == null || _voxelBuffer.count < totalVoxelsInBatch)
        {
            _voxelBuffer?.Release();
            _voxelBuffer = new ComputeBuffer(totalVoxelsInBatch, UnsafeUtility.SizeOf<VoxelData>());
            if (_readbackData.IsCreated) _readbackData.Dispose();
            _readbackData = new NativeArray<VoxelData>(totalVoxelsInBatch, Allocator.Persistent);
            Debug.Log($"[TerrainReadbackSystem] Allocated GPU buffers for batch size {BATCH_SIZE}.");
        }

        using var entities = query.ToEntityArray(Allocator.Temp);
        int numToProcess = math.min(BATCH_SIZE, entities.Length);
        if (numToProcess == 0) return;

        Debug.Log($"[TerrainReadbackSystem] Starting new readback batch for {numToProcess} chunks.");
        _free = false;
        _batchEntities.Clear();

        var cmd = CommandBufferPool.Get("Voxel Generation Batch");
        var compute = resources.VoxelComputeShader;
        int kernel = compute.FindKernel("CSMain");

        uint tx, ty, tz;
        compute.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);
        var groups = (paddedChunkSize + new int3((int)tx - 1, (int)ty - 1, (int)tz - 1)) / new int3((int)tx, (int)ty, (int)tz);

        cmd.SetComputeBufferParam(compute, kernel, "asyncVoxelBuffer", _voxelBuffer);

        for (int i = 0; i < numToProcess; i++)
        {
            Entity entity = entities[i];
            _batchEntities.Add(entity);
            EntityManager.SetComponentEnabled<TerrainChunkRequestReadbackTag>(entity, false);

            var chunk = SystemAPI.GetComponent<Chunk>(entity);
            cmd.SetComputeIntParam(compute, "baseIndex", i * voxelsPerChunk);
            cmd.SetComputeIntParams(compute, "chunkPosition", chunk.Position.x, chunk.Position.y, chunk.Position.z);
            cmd.SetComputeIntParams(compute, "chunkSize", paddedChunkSize.x, paddedChunkSize.y, paddedChunkSize.z);
            cmd.DispatchCompute(compute, kernel, groups.x, groups.y, groups.z);
        }

        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        AsyncGPUReadback.Request(_voxelBuffer, numToProcess * voxelsPerChunk * UnsafeUtility.SizeOf<VoxelData>(), 0, OnVoxelDataReady);
    }

    private void OnVoxelDataReady(AsyncGPUReadbackRequest request)
    {
        if (_disposed) return;

        if (request.hasError)
        {
            Debug.LogError("[TerrainReadbackSystem] GPU Readback Error!");
            ResetState();
            return;
        }

        Debug.Log($"[TerrainReadbackSystem] GPU data received for {_batchEntities.Count} chunks. Scheduling CPU copy jobs.");

        var data = request.GetData<VoxelData>();
        data.CopyTo(_readbackData);

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        for (int i = 0; i < _batchEntities.Count; i++)
        {
            Entity entity = _batchEntities[i];
            if (!SystemAPI.Exists(entity)) continue;

            var voxels = new NativeArray<VoxelData>(voxelsPerChunk, Allocator.Persistent);
            var sourceSlice = _readbackData.GetSubArray(i * voxelsPerChunk, voxelsPerChunk);

            var copyJob = new CopyJob { Source = sourceSlice, Destination = voxels };
            _copyHandles[i] = copyJob.Schedule();

            if (SystemAPI.IsComponentEnabled<TerrainChunkVoxels>(entity))
            {
                SystemAPI.GetComponent<TerrainChunkVoxels>(entity).Dispose();
            }

            SystemAPI.SetComponent(entity, new TerrainChunkVoxels { Voxels = voxels, AsyncWriteJobHandle = _copyHandles[i] });
            SystemAPI.SetComponentEnabled<TerrainChunkVoxels>(entity, true);
        }

        _pendingCopies = JobHandle.CombineDependencies(_copyHandles.Slice(0, _batchEntities.Count));
        _dataFetched = true;
    }

    private void FinalizeBatch()
    {
        Debug.Log($"[TerrainReadbackSystem] Finalizing batch of {_batchEntities.Count} chunks.");
        foreach (var entity in _batchEntities)
        {
            if (!SystemAPI.Exists(entity)) continue;
            SystemAPI.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(entity, true);
        }
        ResetState();
    }

    private void ResetState()
    {
        _free = true;
        _dataFetched = false;
        _pendingCopies = null;
        _batchEntities.Clear();
    }

    [BurstCompile]
    private struct CopyJob : IJob
    {
        [ReadOnly] public NativeSlice<VoxelData> Source;
        [WriteOnly] public NativeArray<VoxelData> Destination;
        public void Execute() => Source.CopyTo(Destination);
    }
}