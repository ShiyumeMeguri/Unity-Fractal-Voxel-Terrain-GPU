// Systems/TerrainReadbackSystem.cs
using Ruri.Voxel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct MultiReadbackTransform
{
    public float3 position;
    public float scale;
}

[BurstCompile]
public unsafe struct GpuToCpuCopy : IJobParallelFor
{
    [WriteOnly]
    public NativeArray<VoxelData> Destination;
    [NativeDisableUnsafePtrRestriction]
    [ReadOnly]
    public VoxelData* Source;

    public void Execute(int index)
    {
        Destination[index] = Source[index];
    }
}


[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ChunkManagerSystem))]
public partial class TerrainReadbackSystem : SystemBase
{
    private const int BATCH_SIZE = 64;
    private const int BATCH_GRID_DIM = 4;

    private bool _free;
    private bool _disposed;
    private bool _countersFetched;
    private bool _voxelsFetched;

    private List<Entity> _batchEntities;
    private JobHandle? _pendingCopies;
    private NativeArray<JobHandle> _copyHandles;

    private NativeArray<VoxelData> _readbackData;
    private NativeArray<int> _multiSignCounters;

    private NativeArray<MultiReadbackTransform> _transforms;
    private ComputeBuffer _voxelBuffer;
    private ComputeBuffer _multiSignCountersBuffer;
    private ComputeBuffer _transformsBuffer;


    protected override void OnCreate()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;
        int totalVoxelsInBatch = voxelsPerChunk * BATCH_SIZE;

        _free = true;
        _disposed = false;
        _batchEntities = new List<Entity>(BATCH_SIZE);
        _copyHandles = new NativeArray<JobHandle>(BATCH_SIZE, Allocator.Persistent);

        _readbackData = new NativeArray<VoxelData>(totalVoxelsInBatch, Allocator.Persistent);
        _multiSignCounters = new NativeArray<int>(BATCH_SIZE, Allocator.Persistent);
        _transforms = new NativeArray<MultiReadbackTransform>(BATCH_SIZE, Allocator.Persistent);

        _voxelBuffer = new ComputeBuffer(totalVoxelsInBatch, UnsafeUtility.SizeOf<VoxelData>());
        _multiSignCountersBuffer = new ComputeBuffer(BATCH_SIZE, sizeof(int));
        _transformsBuffer = new ComputeBuffer(BATCH_SIZE, UnsafeUtility.SizeOf<MultiReadbackTransform>());

    }

    protected override void OnDestroy()
    {
        _disposed = true;
        _pendingCopies?.Complete();
        AsyncGPUReadback.WaitAllRequests();
        if (_copyHandles.IsCreated) _copyHandles.Dispose();
        if (_readbackData.IsCreated) _readbackData.Dispose();
        if (_multiSignCounters.IsCreated) _multiSignCounters.Dispose();
        if (_transforms.IsCreated) _transforms.Dispose();
        _voxelBuffer?.Release();
        _multiSignCountersBuffer?.Release();
        _transformsBuffer?.Release();
    }

    protected override void OnUpdate()
    {
        var query = GetEntityQuery(ComponentType.ReadOnly<Chunk>(), ComponentType.ReadOnly<TerrainChunkRequestReadbackTag>());
        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.readback = query.IsEmpty && _free;

        if (_free)
        {
            TryBeginReadback(query);
        }
        else if (_voxelsFetched && _countersFetched && _pendingCopies.HasValue)
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
        if (resources.VoxelComputeShader == null)
        {
            return;
        }

        using var entities = query.ToEntityArray(Allocator.Temp);
        int numToProcess = math.min(BATCH_SIZE, entities.Length);
        if (numToProcess == 0) return;

        _free = false;
        _voxelsFetched = false;
        _countersFetched = false;
        _pendingCopies = null;
        _batchEntities.Clear();

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var paddedChunkSize = config.PaddedChunkSize;

        for (int i = 0; i < numToProcess; i++)
        {
            Entity entity = entities[i];
            _batchEntities.Add(entity);
            EntityManager.SetComponentEnabled<TerrainChunkRequestReadbackTag>(entity, false);

            var chunk = SystemAPI.GetComponent<Chunk>(entity);
            _transforms[i] = new MultiReadbackTransform
            {
                position = chunk.Position * config.ChunkSize,
                scale = 1.0f // Assuming scale is always 1 for simplicity
            };
        }

        var cmd = CommandBufferPool.Get("Voxel Generation Batch");
        var compute = resources.VoxelComputeShader;
        int kernel = compute.FindKernel("CSMain");

        // Upload transforms and clear counters
        _transformsBuffer.SetData(_transforms, 0, 0, numToProcess);
        _multiSignCountersBuffer.SetData(new int[BATCH_SIZE]);

        cmd.SetComputeBufferParam(compute, kernel, "asyncVoxelBuffer", _voxelBuffer);
        cmd.SetComputeBufferParam(compute, kernel, "multi_transforms_buffer", _transformsBuffer);
        cmd.SetComputeBufferParam(compute, kernel, "multi_counters_buffer", _multiSignCountersBuffer);

        cmd.SetComputeIntParam(compute, "PADDED_CHUNK_SIZE", paddedChunkSize.x);
        cmd.SetComputeIntParam(compute, "PADDED_CHUNK_VOLUME", paddedChunkSize.x * paddedChunkSize.y * paddedChunkSize.z);
        cmd.SetComputeIntParam(compute, "BATCH_GRID_DIM", BATCH_GRID_DIM);
        cmd.SetComputeIntParam(compute, "ChunksInBatch", numToProcess);

        uint tx, ty, tz;
        compute.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);
        int totalDispatchX = paddedChunkSize.x * BATCH_GRID_DIM;
        int totalDispatchY = paddedChunkSize.y * BATCH_GRID_DIM;
        int totalDispatchZ = paddedChunkSize.z * BATCH_GRID_DIM;

        int groupsX = (totalDispatchX + (int)tx - 1) / (int)tx;
        int groupsY = (totalDispatchY + (int)ty - 1) / (int)ty;
        int groupsZ = (totalDispatchZ + (int)tz - 1) / (int)tz;

        cmd.DispatchCompute(compute, kernel, groupsX, groupsY, groupsZ);

        Graphics.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

        AsyncGPUReadback.Request(_voxelBuffer, numToProcess * paddedChunkSize.x * paddedChunkSize.y * paddedChunkSize.z * UnsafeUtility.SizeOf<VoxelData>(), 0, OnVoxelDataReady);
        AsyncGPUReadback.Request(_multiSignCountersBuffer, numToProcess * sizeof(int), 0, OnCountersReady);
    }

    private unsafe void OnVoxelDataReady(AsyncGPUReadbackRequest request)
    {
        if (_disposed) return;

        if (request.hasError)
        {
            ResetState();
            return;
        }

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        VoxelData* sourcePtr = (VoxelData*)request.GetData<VoxelData>().GetUnsafeReadOnlyPtr();

        for (int i = 0; i < _batchEntities.Count; i++)
        {
            Entity entity = _batchEntities[i];
            if (!SystemAPI.Exists(entity)) continue;

            var voxels = new NativeArray<VoxelData>(voxelsPerChunk, Allocator.Persistent);

            var copyJob = new GpuToCpuCopy
            {
                Source = sourcePtr + (i * voxelsPerChunk),
                Destination = voxels
            };

            ref var chunkVoxels = ref SystemAPI.GetComponentRW<TerrainChunkVoxels>(entity).ValueRW;
            if (chunkVoxels.IsCreated) chunkVoxels.Dispose();

            var oldHandle = JobHandle.CombineDependencies(chunkVoxels.AsyncReadJobHandle, chunkVoxels.AsyncWriteJobHandle);
            var handle = copyJob.Schedule(voxels.Length, 256, oldHandle);

            _copyHandles[i] = handle;

            chunkVoxels.Voxels = voxels;
            chunkVoxels.AsyncWriteJobHandle = handle;

            SystemAPI.SetComponentEnabled<TerrainChunkVoxels>(entity, true);
        }

        _pendingCopies = JobHandle.CombineDependencies(_copyHandles.Slice(0, _batchEntities.Count));
        _voxelsFetched = true;
    }

    private void OnCountersReady(AsyncGPUReadbackRequest request)
    {
        if (_disposed) return;
        if (request.hasError)
        {
            ResetState();
            return;
        }

        request.GetData<int>().CopyTo(_multiSignCounters);
        _countersFetched = true;
    }

    private void FinalizeBatch()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        int voxelsPerChunk = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z;

        for (int i = 0; i < _batchEntities.Count; i++)
        {
            Entity entity = _batchEntities[i];
            if (!SystemAPI.Exists(entity)) continue;

            int signCount = _multiSignCounters[i];
            bool isEmptyOrFull = math.abs(signCount) == voxelsPerChunk;
            bool skipIfEmpty = SystemAPI.GetComponent<TerrainChunkRequestReadbackTag>(entity).SkipMeshingIfEmpty;

            SystemAPI.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(entity, true);

            if (isEmptyOrFull && skipIfEmpty)
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
        _free = true;
        _voxelsFetched = false;
        _countersFetched = false;
        _pendingCopies = null;
        _batchEntities.Clear();
    }
}