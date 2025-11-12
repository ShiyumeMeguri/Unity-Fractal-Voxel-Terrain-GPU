// Assets/ScriptsECS/Systems/TerrainMeshingSystem.cs

using OptIn.Voxel;
using OptIn.Voxel.Meshing;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainReadbackSystem))]
public partial class TerrainMeshingSystem : SystemBase
{
    private List<MeshJobHandler> m_Handlers;
    private EntitiesGraphicsSystem m_GraphicsSystem;
    private BatchMaterialID m_MainMeshMaterialID;
    private BatchMaterialID m_SkirtMeshMaterialID;
    private bool m_IsInitialized;

    private static readonly RenderMeshDescription RenderMeshDescription = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainMesherConfig>();
        RequireForUpdate<TerrainResources>();
        m_GraphicsSystem = World.GetOrCreateSystemManaged<EntitiesGraphicsSystem>();
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.TryGetSingleton<TerrainMesherConfig>(out var mesherConfig)) return;

        if (!m_IsInitialized)
        {
            Initialize(mesherConfig);
        }

        ref var readySystems = ref SystemAPI.GetSingletonRW<TerrainReadySystems>().ValueRW;
        readySystems.MeshingSystemReady = m_Handlers.All(h => h.IsFree);

        foreach (var handler in m_Handlers)
        {
            if (handler.IsComplete(EntityManager))
            {
                FinishJob(handler);
            }
        }

        var query = GetEntityQuery(
            ComponentType.ReadOnly<TerrainChunkRequestMeshingTag>(),
            ComponentType.ReadOnly<TerrainChunkVoxelsReadyTag>(),
            ComponentType.ReadWrite<ChunkVoxelData>(),
            ComponentType.ReadOnly<Chunk>()
            );
        if (query.IsEmpty) return;

        var freeHandlers = m_Handlers.Where(h => h.IsFree).ToArray();
        int numToProcess = math.min(freeHandlers.Length, query.CalculateEntityCount());
        if (numToProcess == 0) return;

        using var entities = query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < numToProcess; i++)
        {
            var handler = freeHandlers[i];
            var entity = entities[i];

            ref var voxels = ref SystemAPI.GetComponentRW<ChunkVoxelData>(entity).ValueRW;
            handler.BeginJob(entity, ref voxels, EntityManager, Dependency);

            SystemAPI.SetComponentEnabled<TerrainChunkEndOfPipeTag>(entity, false);
            SystemAPI.SetComponentEnabled<TerrainChunkRequestMeshingTag>(entity, false);
        }
    }

    private void Initialize(TerrainMesherConfig mesherConfig)
    {
        var terrainConfig = SystemAPI.GetSingleton<TerrainConfig>();
        var resources = SystemAPI.ManagedAPI.GetSingleton<TerrainResources>();

        m_Handlers = new List<MeshJobHandler>(mesherConfig.MeshJobsPerTick);
        for (int i = 0; i < mesherConfig.MeshJobsPerTick; i++)
        {
            var handler = new MeshJobHandler();
            handler.Init(terrainConfig);
            m_Handlers.Add(handler);
        }

        var skirtMaterial = new Material(resources.ChunkMaterial);
        // You might need a special shader variant for skirts
        // skirtMaterial.EnableKeyword("_SKIRT_ON"); 

        m_MainMeshMaterialID = m_GraphicsSystem.RegisterMaterial(resources.ChunkMaterial);
        m_SkirtMeshMaterialID = m_GraphicsSystem.RegisterMaterial(skirtMaterial);

        m_IsInitialized = true;
    }

    private void FinishJob(MeshJobHandler handler)
    {
        if (handler.TryComplete(EntityManager, out Mesh mesh, out Entity entity, out var stats))
        {
            if (!SystemAPI.Exists(entity))
            {
                if (mesh != null) Object.Destroy(mesh);
                return;
            }

            SystemAPI.SetComponentEnabled<TerrainChunkEndOfPipeTag>(entity, true);
            var chunk = SystemAPI.GetComponent<Chunk>(entity);

            if (SystemAPI.IsComponentEnabled<TerrainChunkMesh>(entity))
            {
                SystemAPI.GetComponent<TerrainChunkMesh>(entity).Dispose();
            }

            if (stats.IsEmpty)
            {
                if (SystemAPI.HasComponent<MaterialMeshInfo>(entity))
                    SystemAPI.SetComponentEnabled<MaterialMeshInfo>(entity, false);
                if (mesh != null) Object.Destroy(mesh);
                return;
            }

            var meshID = m_GraphicsSystem.RegisterMesh(mesh);

            var mainMaterialMeshInfo = new MaterialMeshInfo
            {
                MaterialID = m_MainMeshMaterialID,
                MeshID = meshID,
                SubMesh = 0
            };
            RenderMeshUtility.AddComponents(entity, EntityManager, RenderMeshDescription, mainMaterialMeshInfo);

            AABB aabb = new AABB { Center = stats.Bounds.center, Extents = stats.Bounds.extents };
            SystemAPI.SetComponent(entity, new RenderBounds { Value = aabb });

            var chunkTransform = SystemAPI.GetComponent<LocalToWorld>(entity);
            AABB worldAABB = TransformAABB(chunkTransform.Value, aabb);
            SystemAPI.SetComponent(entity, new WorldRenderBounds { Value = worldAABB });

            for (ushort i = 0; i < 6; i++)
            {
                if (i >= chunk.Skirts.Length) continue;
                var skirtEntity = chunk.Skirts[i];
                if (!SystemAPI.Exists(skirtEntity)) continue;

                var skirtMaterialMeshInfo = new MaterialMeshInfo
                {
                    MaterialID = m_SkirtMeshMaterialID,
                    MeshID = meshID,
                    SubMesh = (ushort)(i + 1)
                };
                RenderMeshUtility.AddComponents(skirtEntity, EntityManager, RenderMeshDescription, skirtMaterialMeshInfo);
                SystemAPI.SetComponent(skirtEntity, new RenderBounds { Value = aabb });
                SystemAPI.SetComponent(skirtEntity, new WorldRenderBounds { Value = worldAABB });
                SystemAPI.SetComponentEnabled<TerrainDeferredVisible>(skirtEntity, BitUtils.IsBitSet(chunk.SkirtMask, i));
            }

            SystemAPI.SetComponentEnabled<TerrainChunkMesh>(entity, true);
            SystemAPI.SetComponent(entity, TerrainChunkMesh.FromJobHandler(
                stats.Vertices.positions, // Pass NativeArray<float3> directly
                stats.MainMeshIndices,
                stats.VertexCount,
                stats.MainMeshIndexCount,
                Dependency)
            );

            SystemAPI.SetComponentEnabled<TerrainChunkRequestCollisionTag>(entity, true);
        }
    }

    private AABB TransformAABB(float4x4 matrix, AABB localAabb)
    {
        float3 center = math.transform(matrix, localAabb.Center);
        float3 extents = math.abs(matrix.c0.xyz) * localAabb.Extents.x +
                         math.abs(matrix.c1.xyz) * localAabb.Extents.y +
                         math.abs(matrix.c2.xyz) * localAabb.Extents.z;
        return new AABB { Center = center, Extents = extents };
    }

    protected override void OnDestroy()
    {
        if (m_Handlers != null)
        {
            foreach (var handler in m_Handlers)
                handler.Dispose();
        }
    }
}