using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MeshingSystem))]
public partial class ColliderBakingSystem : SystemBase
{
    private EntityQuery query;

    protected override void OnCreate()
    {
        query = GetEntityQuery(
            ComponentType.ReadOnly<GeneratedMesh>(),
            ComponentType.ReadOnly<RequestColliderBakeTag>()
        );
        RequireForUpdate(query);
    }

    protected override void OnUpdate()
    {
        // 使用 SystemAPI.Query 来替代过时的 Entities.ForEach
        foreach (var entity in query.ToEntityArray(Allocator.Temp))
        {
            var generatedMesh = EntityManager.GetComponentData<GeneratedMesh>(entity);
            var mesh = generatedMesh.Mesh;

            if (mesh == null || mesh.vertexCount == 0)
            {
                EntityManager.SetComponentEnabled<RequestColliderBakeTag>(entity, false);
                EntityManager.RemoveComponent<GeneratedMesh>(entity);
                continue;
            }

            // Corrected: Removed 'using' to make the NativeArray modifiable.
            var vertices = new NativeArray<float3>(mesh.vertexCount, Allocator.TempJob);
            var triangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);

            try
            {
                // Manual conversion from Vector3[] to NativeArray<float3>
                var sourceVerts = mesh.vertices;
                for (int i = 0; i < sourceVerts.Length; i++)
                {
                    vertices[i] = sourceVerts[i]; // This line now works.
                }

                var colliderBlob = Unity.Physics.MeshCollider.Create(
                    vertices,
                    triangles.Reinterpret<int3>(sizeof(int))
                );

                EntityManager.AddComponentData(entity, new PhysicsCollider { Value = colliderBlob });
            }
            finally
            {
                // Ensure disposal even if an error occurs.
                vertices.Dispose();
                triangles.Dispose();
            }

            EntityManager.SetComponentEnabled<RequestColliderBakeTag>(entity, false);
            EntityManager.RemoveComponent<GeneratedMesh>(entity);
        }
    }
}