using OptIn.Voxel;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PaddingUpdateSystem))]
public partial class MeshingSystem : SystemBase
{
    private EntityQuery meshingQuery;

    protected override void OnCreate()
    {
        meshingQuery = GetEntityQuery(
            // [修正] 查询 ChunkVoxelData 而不是 VoxelBufferElement
            ComponentType.ReadOnly<ChunkVoxelData>(),
            ComponentType.ReadOnly<RequestMeshTag>()
        );
        RequireForUpdate<TerrainConfig>();
    }

    protected override void OnUpdate()
    {
        if (meshingQuery.IsEmpty) return;

        var config = SystemAPI.GetSingleton<TerrainConfig>();
        var resources = SystemAPI.ManagedAPI.GetSingleton<TerrainResources>();

        var ecb = new EntityCommandBuffer(Allocator.TempJob);

        // [修正] 查询 ChunkVoxelData 并使用 WithEntityAccess
        foreach (var (voxelData, entity) in SystemAPI.Query<ChunkVoxelData>().WithAll<RequestMeshTag>().WithEntityAccess())
        {
            if (!SystemAPI.IsComponentEnabled<RequestMeshTag>(entity) || !voxelData.IsCreated) continue;

            var meshData = new VoxelMeshBuilder.NativeMeshData(config.ChunkSize + 2);

            // 直接传递从组件中获取的 NativeArray
            var jobHandle = VoxelMeshBuilder.ScheduleMeshingJob(
                voxelData.Voxels,
                config.ChunkSize + 2,
                meshData,
                Dependency);

            jobHandle.Complete();

            meshData.GetMeshInformation(out int vertexCount, out int indexCount);

            if (vertexCount > 0 && indexCount > 0)
            {
                var mesh = new Mesh { name = "ChunkMesh", indexFormat = IndexFormat.UInt32 };

                var vertexAttributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp);
                vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
                vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
                vertexAttributes[2] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4);

                mesh.SetVertexBufferParams(vertexCount, vertexAttributes);
                vertexAttributes.Dispose();

                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

                mesh.SetVertexBufferData(meshData.nativeVertices, 0, 0, vertexCount);
                mesh.SetIndexBufferData(meshData.nativeIndices, 0, 0, indexCount);

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

            meshData.Dispose();

            ecb.SetComponentEnabled<RequestMeshTag>(entity, false);
            ecb.SetComponentEnabled<IdleTag>(entity, true);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}