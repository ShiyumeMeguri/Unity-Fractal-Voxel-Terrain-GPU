using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;

namespace Ruri.Voxel
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TerrainMeshingSystem))]
    [BurstCompile]
    public partial struct TerrainColliderSystem : ISystem
    {
        private NativeList<PendingBakeRequest> _PendingRequests;

        private struct PendingBakeRequest
        {
            public JobHandle Dependency;
            public Entity Entity;
            public NativeReference<BlobAssetReference<Collider>> ColliderRef;

            public void Dispose()
            {
                Dependency.Complete();
                if (ColliderRef.IsCreated && ColliderRef.Value.IsCreated) ColliderRef.Value.Dispose();
                if (ColliderRef.IsCreated) ColliderRef.Dispose();
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _PendingRequests = new NativeList<PendingBakeRequest>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();
            foreach (var request in _PendingRequests)
            {
                request.Dispose();
            }
            _PendingRequests.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            for (int i = _PendingRequests.Length - 1; i >= 0; i--)
            {
                var request = _PendingRequests[i];
                if (request.Dependency.IsCompleted)
                {
                    request.Dependency.Complete();
                    if (SystemAPI.Exists(request.Entity))
                    {
                        if (SystemAPI.HasComponent<PhysicsCollider>(request.Entity))
                        {
                            var oldCollider = SystemAPI.GetComponent<PhysicsCollider>(request.Entity);
                            if (oldCollider.Value.IsCreated) oldCollider.Value.Dispose();
                        }
                        else
                        {
                            state.EntityManager.AddComponent<PhysicsCollider>(request.Entity);
                        }

                        SystemAPI.SetComponent(request.Entity, new PhysicsCollider { Value = request.ColliderRef.Value });
                    }
                    else
                    {
                        if (request.ColliderRef.Value.IsCreated) request.ColliderRef.Value.Dispose();
                    }

                    request.ColliderRef.Dispose();
                    _PendingRequests.RemoveAtSwapBack(i);
                }
            }

            foreach (var (meshData, entity) in SystemAPI.Query<RefRO<TerrainChunkMesh>>().WithAll<TerrainChunkRequestCollisionTag>().WithEntityAccess())
            {
                if (!meshData.ValueRO.Vertices.IsCreated || meshData.ValueRO.Vertices.Length == 0)
                {
                    SystemAPI.SetComponentEnabled<TerrainChunkRequestCollisionTag>(entity, false);
                    continue;
                }

                var colliderRef = new NativeReference<BlobAssetReference<Collider>>(Allocator.Persistent);
                var bakeJob = new BakingJob
                {
                    Vertices = meshData.ValueRO.Vertices,
                    Indices = meshData.ValueRO.MainMeshIndices,
                    Collider = colliderRef
                };

                var handle = bakeJob.Schedule(meshData.ValueRO.AccessJobHandle);

                _PendingRequests.Add(new PendingBakeRequest
                {
                    Dependency = handle,
                    Entity = entity,
                    ColliderRef = colliderRef
                });

                SystemAPI.SetComponentEnabled<TerrainChunkRequestCollisionTag>(entity, false);
            }
        }

        [BurstCompile]
        private struct BakingJob : IJob
        {
            [ReadOnly] public NativeArray<float3> Vertices;
            [ReadOnly] public NativeArray<int> Indices;
            public NativeReference<BlobAssetReference<Collider>> Collider;

            public void Execute()
            {
                if (Vertices.Length == 0 || Indices.Length == 0) return;

                var triangles = Indices.Reinterpret<int3>(sizeof(int));
                var material = Material.Default;
                Collider.Value = Unity.Physics.MeshCollider.Create(Vertices, triangles, CollisionFilter.Default, material);
            }
        }
    }
}