using OptIn.Voxel;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PaddingUpdateSystem))]
public partial class MeshingSystem : SystemBase
{
    private EntityQuery m_PendingMeshQuery;

    protected override void OnCreate()
    {
        // 查询等待网格生成的实体
        m_PendingMeshQuery = GetEntityQuery(
            ComponentType.ReadOnly<PendingMeshTag>(),
            ComponentType.ReadOnly<MeshingJobData>()
        );
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<RequestMeshTag>(), ComponentType.ReadOnly<ChunkVoxelData>()));
    }

    protected override void OnUpdate()
    {
        // --- 1. 调度新的网格生成作业 ---
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        var paddedChunkSize = config.ChunkSize + 2;
        var dependency = Dependency;

        foreach (var (voxelData, entity) in SystemAPI.Query<RefRO<ChunkVoxelData>>().WithAll<RequestMeshTag>().WithEntityAccess())
        {
            if (!voxelData.ValueRO.IsCreated) continue;

            var meshData = new VoxelMeshBuilder.NativeMeshData(paddedChunkSize);
            var jobHandle = VoxelMeshBuilder.ScheduleMeshingJob(
                voxelData.ValueRO.Voxels,
                paddedChunkSize,
                meshData,
                dependency);

            ecb.AddComponent(entity, new MeshingJobData { JobHandle = jobHandle, MeshData = meshData });
            ecb.SetComponentEnabled<RequestMeshTag>(entity, false);
            ecb.SetComponentEnabled<PendingMeshTag>(entity, true);
        }

        // --- 2. 在主线程上处理已完成的作业 ---
        // 由于需要处理托管类型 (Mesh, Material) 并进行结构性变更，这部分必须在主线程上运行

        // 如果没有待处理的网格，则提前返回
        if (m_PendingMeshQuery.IsEmpty)
        {
            return;
        }

        var resources = SystemAPI.ManagedAPI.GetSingleton<TerrainResources>();

        // 将所有待处理的实体收集到一个临时数组中，以避免在迭代时修改集合
        using var pendingEntities = m_PendingMeshQuery.ToEntityArray(Allocator.Temp);

        foreach (var entity in pendingEntities)
        {
            // 必须在循环内部获取最新的组件状态
            var jobData = EntityManager.GetComponentObject<MeshingJobData>(entity);

            // 如果作业尚未完成，则跳过
            if (!jobData.JobHandle.IsCompleted) continue;

            jobData.JobHandle.Complete();

            jobData.MeshData.GetMeshInformation(out int vertexCount, out int indexCount);

            // 清理旧的渲染组件
            if (EntityManager.HasComponent<RenderMeshArray>(entity))
            {
                EntityManager.RemoveComponent<RenderMeshArray>(entity);
                EntityManager.RemoveComponent<MaterialMeshInfo>(entity);
                EntityManager.RemoveComponent<RenderBounds>(entity);
                EntityManager.RemoveComponent<WorldRenderBounds>(entity);
            }

            if (vertexCount > 0 && indexCount > 0)
            {
                var mesh = new Mesh { name = "ChunkMesh", indexFormat = IndexFormat.UInt32 };

                var vertexAttributes = new[]
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4)
                };

                mesh.SetVertexBufferParams(vertexCount, vertexAttributes);
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

                mesh.SetVertexBufferData(jobData.MeshData.nativeVertices, 0, 0, vertexCount);
                mesh.SetIndexBufferData(jobData.MeshData.nativeIndices, 0, 0, indexCount);

                mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshUpdateFlags.DontRecalculateBounds);
                mesh.RecalculateBounds();

                var renderMesh = new RenderMeshArray(new[] { resources.ChunkMaterial }, new[] { mesh });
                var renderMeshDesc = new RenderMeshDescription(ShadowCastingMode.On, true);

                // 使用官方工具函数添加所有渲染组件
                RenderMeshUtility.AddComponents(
                    entity,
                    EntityManager,
                    renderMeshDesc,
                    renderMesh,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                );

                // 添加我们自己的托管组件和标签
                EntityManager.AddComponentData(entity, new GeneratedMesh { Mesh = mesh });
                EntityManager.SetComponentEnabled<RequestColliderBakeTag>(entity, true);
            }

            // 作业完成，清理并更新状态
            jobData.Dispose();
            EntityManager.RemoveComponent<MeshingJobData>(entity);
            EntityManager.SetComponentEnabled<PendingMeshTag>(entity, false);
            EntityManager.SetComponentEnabled<IdleTag>(entity, true);
        }
    }
}