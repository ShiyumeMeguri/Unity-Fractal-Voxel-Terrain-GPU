using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MeshingSystem))]
public partial class ColliderBakingSystem : SystemBase
{
    private EndSimulationEntityCommandBufferSystem m_EndSimECBSystem;

    protected override void OnCreate()
    {
        // [修复] 在 OnCreate 中获取 ECB 系统
        m_EndSimECBSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        RequireForUpdate(GetEntityQuery(
            ComponentType.ReadOnly<GeneratedMesh>(),
            ComponentType.ReadOnly<RequestColliderBakeTag>()
        ));
    }

    protected override void OnUpdate()
    {
        var ecb = m_EndSimECBSystem.CreateCommandBuffer();

        // [修复] 使用 foreach 替代错误的 .WithoutBurst().Run()
        foreach (var (generatedMesh, entity) in SystemAPI.Query<GeneratedMesh>().WithAll<RequestColliderBakeTag>().WithEntityAccess())
        {
            var mesh = generatedMesh.Mesh;

            if (mesh == null || mesh.vertexCount == 0)
            {
                ecb.SetComponentEnabled(entity, ComponentType.ReadWrite<RequestColliderBakeTag>(), false);
                ecb.RemoveComponent<GeneratedMesh>(entity);
                continue;
            }

            var verticesList = new List<Vector3>();
            mesh.GetVertices(verticesList);
            var vertices = new NativeArray<float3>(verticesList.Count, Allocator.TempJob);
            for (int i = 0; i < verticesList.Count; i++) vertices[i] = verticesList[i];

            var triangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);

            var colliderBlob = Unity.Physics.MeshCollider.Create(
                vertices,
                triangles.Reinterpret<int3>(sizeof(int))
            );

            if (SystemAPI.HasComponent<PhysicsCollider>(entity))
            {
                if (SystemAPI.GetComponent<PhysicsCollider>(entity).Value.IsCreated)
                {
                    SystemAPI.GetComponentRW<PhysicsCollider>(entity).ValueRW.Value.Dispose();
                }
                ecb.SetComponent(entity, new PhysicsCollider { Value = colliderBlob });
            }
            else
            {
                ecb.AddComponent(entity, new PhysicsCollider { Value = colliderBlob });
            }

            Dependency = JobHandle.CombineDependencies(
                vertices.Dispose(Dependency),
                triangles.Dispose(Dependency)
            );

            ecb.SetComponentEnabled<RequestColliderBakeTag>(entity, false);
            ecb.RemoveComponent<GeneratedMesh>(entity);
        }

        // [修复] 将依赖添加到ECB系统
        m_EndSimECBSystem.AddJobHandleForProducer(Dependency);
    }
}