using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ruri.Voxel
{
    [UpdateInGroup(typeof(TerrainFixedStepSystemGroup))]
    // [修正] 移除对TerrainManagerSystem的依赖，因为我们还未完全实现它
    public partial class TerrainReadbackSystem : SystemBase
    {
        private bool free;
        private NativeArray<VoxelData> multiData; // [核心修改] 使用VoxelData
        private List<Entity> entities;
        private JobHandle? pendingCopies;
        private NativeArray<JobHandle> copies;
        private NativeArray<int> multiSignCounters;
        private NativeArray<MultiReadbackTransform> transforms;
        private bool countersFetched, voxelsFetched;
        private bool disposed;
        private MultiReadbackExecutor multiExecutor;
        private ComputeBuffer multiSignCountersBuffer;

        // [新增] 存储对ComputeShader的引用
        private ComputeShader _VoxelComputeShader;

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainReadbackConfig>();
            RequireForUpdate<TerrainReadySystems>();
            // [新增] 获取资源单例
            RequireForUpdate<TerrainResources>();

            multiData = new NativeArray<VoxelData>(VoxelUtils.VOLUME * VoxelUtils.MULTI_READBACK_CHUNK_COUNT, Allocator.Persistent);
            entities = new List<Entity>(VoxelUtils.MULTI_READBACK_CHUNK_COUNT);
            copies = new NativeArray<JobHandle>(VoxelUtils.MULTI_READBACK_CHUNK_COUNT, Allocator.Persistent);
            multiSignCounters = new NativeArray<int>(VoxelUtils.MULTI_READBACK_CHUNK_COUNT, Allocator.Persistent);
            transforms = new NativeArray<MultiReadbackTransform>(VoxelUtils.MULTI_READBACK_CHUNK_COUNT, Allocator.Persistent);

            Reset();
            disposed = false;

            multiExecutor = new MultiReadbackExecutor();
            multiSignCountersBuffer = new ComputeBuffer(VoxelUtils.MULTI_READBACK_CHUNK_COUNT, sizeof(int), ComputeBufferType.Structured);
        }

        private void Reset()
        {
            free = true;
            entities.Clear();
            pendingCopies = null;
            copies.AsSpan().Fill(default);
            voxelsFetched = false;
            countersFetched = false;
        }

        protected override void OnDestroy()
        {
            disposed = true;
            pendingCopies?.Complete();
            AsyncGPUReadback.WaitAllRequests();

            if (multiData.IsCreated) multiData.Dispose();
            if (multiSignCounters.IsCreated) multiSignCounters.Dispose();
            if (copies.IsCreated) copies.Dispose();
            if (transforms.IsCreated) transforms.Dispose();

            multiExecutor.DisposeResources();
            multiSignCountersBuffer?.Dispose();
        }

        protected override void OnUpdate()
        {
            // 在第一次更新时获取ComputeShader
            if (_VoxelComputeShader == null)
            {
                if (SystemAPI.ManagedAPI.TryGetSingleton<TerrainResources>(out var resources))
                {
                    _VoxelComputeShader = resources.VoxelComputeShader;
                }
                else
                {
                    return; // 等待资源就绪
                }
            }

            var query = SystemAPI.QueryBuilder().WithAll<TerrainChunkVoxels, Chunk, TerrainChunkRequestReadbackTag>().Build();
            bool ready = query.IsEmpty && free;

            ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
            readySystems.readback = ready;

            if (free)
            {
                TryBeginReadback(query);
            }
            else
            {
                TryCheckIfReadbackComplete();
            }
        }

        private void TryBeginReadback(EntityQuery query)
        {
            using var entitiesArray = query.ToEntityArray(Allocator.Temp);
            if (entitiesArray.Length == 0) return;

            int numChunks = math.min(VoxelUtils.MULTI_READBACK_CHUNK_COUNT, entitiesArray.Length);
            free = false;
            entities.Clear();

            var terrainConfig = SystemAPI.GetSingleton<TerrainConfig>();

            for (int j = 0; j < numChunks; j++)
            {
                Entity entity = entitiesArray[j];
                entities.Add(entity);

                // [修正] 从Chunk组件获取位置信息，而不是OctreeNode
                var chunk = SystemAPI.GetComponent<Chunk>(entity);
                float3 pos = chunk.Position * terrainConfig.ChunkSize;
                float scale = 1.0f; // 简化模型，没有LOD，所以缩放是1

                transforms[j] = new MultiReadbackTransform { scale = scale, position = pos };
                SystemAPI.SetComponentEnabled<TerrainChunkRequestReadbackTag>(entity, false);
            }

            var parameters = new MultiReadbackExecutorParameters()
            {
                commandBufferName = "Readback Async Dispatch",
                transforms = transforms.GetSubArray(0, numChunks),
                kernelName = "CSMain", // 对应你的Compute Shader内核名
                updateInjected = false,
                multiSignCountersBuffer = multiSignCountersBuffer,
                seed = SystemAPI.GetSingleton<TerrainSeed>(),
            };

            // [修正] Execute现在需要传入ComputeShader对象
            GraphicsFence fence = multiExecutor.Execute(_VoxelComputeShader, parameters);

            // [简化] 移除了对CSLayers的调用

            var cmds = new CommandBuffer { name = "Terrain Readback System Async Readback" };
            cmds.WaitOnAsyncGraphicsFence(fence, SynchronisationStageFlags.ComputeProcessing);

            var voxelData = multiData;
            cmds.RequestAsyncReadback(multiExecutor.Buffers["voxels"].buffer, (AsyncGPUReadbackRequest request) =>
            {
                if (disposed || request.hasError) return;

                unsafe
                {
                    var pointer = (VoxelData*)request.GetData<VoxelData>().GetUnsafeReadOnlyPtr();
                    for (int j = 0; j < entities.Count; j++)
                    {
                        Entity entity = entities[j];
                        if (!SystemAPI.Exists(entity) || !SystemAPI.IsComponentEnabled<TerrainChunkVoxels>(entity)) continue;

                        var src = pointer + (VoxelUtils.VOLUME * j);
                        ref var voxels = ref SystemAPI.GetComponentRW<TerrainChunkVoxels>(entity).ValueRW;

                        var dep = JobHandle.CombineDependencies(voxels.asyncReadJobHandle, voxels.asyncWriteJobHandle);
                        var handle = new GpuToCpuCopy
                        {
                            cpuData = voxels.data,
                            rawGpuData = src,
                        }.Schedule(VoxelUtils.VOLUME, 256, dep);

                        copies[j] = handle;
                        voxels.asyncWriteJobHandle = handle;
                    }
                    pendingCopies = JobHandle.CombineDependencies(copies.Slice(0, entities.Count));
                }
                voxelsFetched = true;
            });

            cmds.RequestAsyncReadback(multiSignCountersBuffer, (AsyncGPUReadbackRequest request) =>
            {
                if (disposed || request.hasError) return;
                multiSignCounters.CopyFrom(request.GetData<int>());
                countersFetched = true;
            });

            Graphics.ExecuteCommandBuffer(cmds);
            cmds.Dispose();
        }

        private void TryCheckIfReadbackComplete()
        {
            if (pendingCopies.HasValue && pendingCopies.Value.IsCompleted && countersFetched && voxelsFetched)
            {
                pendingCopies.Value.Complete();

                for (int j = 0; j < entities.Count; j++)
                {
                    Entity entity = entities[j];
                    if (!SystemAPI.Exists(entity)) continue;

                    // 简单起见，我们总是尝试为非空区块生成网格
                    bool skipMeshing = false;

                    // [简化] 逻辑简化，不依赖复杂的 empty/skipIfEmpty 判断
                    // if (empty && skipIfEmpty) ...

                    SystemAPI.SetComponentEnabled<TerrainChunkVoxelsReadyTag>(entity, true);
                    if (skipMeshing)
                    {
                        SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, false);
                        SystemAPI.SetComponentEnabled<TerrainChunkEndOfPipeTag>(entity, true);
                    }
                    else
                    {
                        SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, true);
                    }
                }
                Reset();
            }
        }
    }
}