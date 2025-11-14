// Behaviours/ManagedTerrainDebugger.cs
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Ruri.Voxel
{
    public class ManagedTerrainDebugger : MonoBehaviour
    {
        public bool debugGui;
        public bool debugChunkBounds;

        private World world;

        private void Start()
        {
            // 遵循框架，在MonoBehaviour中通过World.DefaultGameObjectInjectionWorld访问ECS世界
            world = World.DefaultGameObjectInjectionWorld;
        }

        private void OnGUI()
        {
            if (!debugGui || world == null || !world.IsCreated)
                return;

            var offset = 0;
            var cachedLabels = new List<string>();
            void Label(string text)
            {
                cachedLabels.Add(text);
                offset += 15;
            }

            void MakeBackgroundOpaque()
            {
                for (int i = 0; i < 3; i++) // Draw a few times for opacity
                {
                    GUI.Box(new Rect(0, 0, 350, offset + 20), "");
                }
            }

            // [修正] 查询正确的组件类型 Ruri.Voxel.Chunk
            EntityQuery totalChunks = world.EntityManager.CreateEntityQuery(typeof(Chunk));
            EntityQuery chunksAwaitingGpuData = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkRequestReadbackTag));
            EntityQuery chunksAwaitingMeshing = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkRequestMeshingTag));
            EntityQuery meshedChunks = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkMesh));
            EntityQuery chunksEndOfPipe = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkEndOfPipeTag));
            EntityQuery readySystemsQuery = world.EntityManager.CreateEntityQuery(typeof(TerrainReadySystems));

            GUI.contentColor = Color.white;
            Label($"--- Ruri Voxel ECS Debug ---");
            Label($"# Total Chunk Entities: {totalChunks.CalculateEntityCount()}");
            Label($"# Chunks Pending GPU Voxel Data: {chunksAwaitingGpuData.CalculateEntityCount()}");
            Label($"# Chunks Pending Meshing: {chunksAwaitingMeshing.CalculateEntityCount()}");
            Label($"# Chunks with a Mesh: {meshedChunks.CalculateEntityCount()}");
            Label($"# Chunks in \"End of Pipe\": {chunksEndOfPipe.CalculateEntityCount()}");
            Label($"");

            // [修正] 确保查询单例存在
            if (readySystemsQuery.HasSingleton<TerrainReadySystems>())
            {
                TerrainReadySystems ready = readySystemsQuery.GetSingleton<TerrainReadySystems>();
                Label($"Manager System Ready: " + ready.manager);
                Label($"Readback System Ready: " + ready.readback);
                Label($"Mesher System Ready: " + ready.mesher);
            }
            else
            {
                Label("TerrainReadySystems singleton not found.");
            }

            MakeBackgroundOpaque();

            offset = 0;
            foreach (var item in cachedLabels)
            {
                GUI.Label(new Rect(5, offset, 340, 30), item);
                offset += 15;
            }
        }

        private void OnDrawGizmos()
        {
            if (!debugChunkBounds || world == null || !world.IsCreated)
                return;

            // [修正] 查询正确的组件类型 Ruri.Voxel.Chunk
            EntityQuery meshedChunksQuery = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkMesh), typeof(WorldRenderBounds));

            Gizmos.color = Color.green;
            using var visibleBounds = meshedChunksQuery.ToComponentDataArray<WorldRenderBounds>(Allocator.Temp);

            foreach (var chunk in visibleBounds)
            {
                // [修正] 添加必要的类型转换
                Gizmos.DrawWireCube((Vector3)chunk.Value.Center, (Vector3)chunk.Value.Extents * 2);
            }
        }
    }
}