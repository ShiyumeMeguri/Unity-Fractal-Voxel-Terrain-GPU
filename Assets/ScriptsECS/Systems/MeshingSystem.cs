using OptIn.Voxel;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PaddingUpdateSystem))]
public partial class MeshingSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem m_EndSimECBSystem;

    protected override void OnCreate()
    {
        m_EndSimECBSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        RequireForUpdate<TerrainConfig>();
        RequireForUpdate<TerrainResources>();
        RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<RequestMeshTag>()));
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var ecb = m_EndSimECBSystem.CreateCommandBuffer();
        var paddedChunkSize = config.ChunkSize + 2;
        var dependency = Dependency;

        // --- 1. 调度网格生成作业 ---
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

        Dependency = dependency; // 保存依赖
        m_EndSimECBSystem.AddJobHandleForProducer(Dependency);

        // --- 2. 在主线程上处理已完成的作业 ---
        var resources = SystemAPI.ManagedAPI.GetSingleton<TerrainResources>();

        // [修复] 使用 foreach 替代错误的 .WithoutBurst().Run()
        foreach (var (jobData, entity) in SystemAPI.Query<MeshingJobData>().WithAll<PendingMeshTag>().WithEntityAccess())
        {
            if (!jobData.JobHandle.IsCompleted) continue;

            jobData.JobHandle.Complete();

            jobData.MeshData.GetMeshInformation(out int vertexCount, out int indexCount);

            if (EntityManager.HasComponent<MaterialMeshInfo>(entity))
            {
                // [修复] 使用 ECB 移除渲染组件，替代不存在的 RemoveComponents
                ecb.RemoveComponent<RenderMeshArray>(entity);
                ecb.RemoveComponent<MaterialMeshInfo>(entity);
                ecb.RemoveComponent<RenderBounds>(entity);
                ecb.RemoveComponent<WorldRenderBounds>(entity);
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

                RenderMeshUtility.AddComponents(
                    entity,
                    EntityManager,
                    renderMeshDesc,
                    renderMesh,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
                );

                ecb.AddComponent(entity, new GeneratedMesh { Mesh = mesh });
                ecb.SetComponentEnabled<RequestColliderBakeTag>(entity, true);
            }

            jobData.Dispose();
            ecb.RemoveComponent<MeshingJobData>(entity);
            ecb.SetComponentEnabled<PendingMeshTag>(entity, false);
            ecb.SetComponentEnabled<IdleTag>(entity, true);
        }
    }
}