// Assets/ScriptsECS/Behaviours/ManagedTerrainDebugger.cs
using Ruri.Voxel; // 修正命名空间
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
                for (int i = 0; i < 5; i++)
                {
                    GUI.Box(new Rect(0, 0, 350, offset + 20), "");
                }
            }

            EntityQuery totalChunks = world.EntityManager.CreateEntityQuery(typeof(Chunk));
            EntityQuery chunksAwaitingGpuData = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkRequestReadbackTag));
            EntityQuery chunksAwaitingMeshing = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkRequestMeshingTag));
            EntityQuery meshedChunks = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkMesh));
            EntityQuery chunksEndOfPipe = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkEndOfPipeTag));
            EntityQuery readySystems = world.EntityManager.CreateEntityQuery(typeof(TerrainReadySystems));

            GUI.contentColor = Color.white;
            Label($"--- Ruri Voxel ECS Debug ---");
            Label($"# Total Chunk Entities: {totalChunks.CalculateEntityCount()}");
            Label($"# Chunks Pending GPU Voxel Data: {chunksAwaitingGpuData.CalculateEntityCount()}");
            Label($"# Chunks Pending Meshing: {chunksAwaitingMeshing.CalculateEntityCount()}");
            Label($"# Chunks with a Mesh: {meshedChunks.CalculateEntityCount()}");
            Label($"# Chunks in \"End of Pipe\": {chunksEndOfPipe.CalculateEntityCount()}");
            Label($"");

            if (readySystems.HasSingleton<TerrainReadySystems>())
            {
                TerrainReadySystems ready = readySystems.GetSingleton<TerrainReadySystems>();
                Label($"Manager System Ready: " + ready.manager);
                Label($"Readback System Ready: " + ready.readback);
                Label($"Mesher System Ready: " + ready.mesher);
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
            if (world == null || !world.IsCreated)
                return;

            // 严格遵循原框架的查询和绘制逻辑，但移除 Occlusion 和 Segment
            if (debugChunkBounds)
            {
                // 创建一个查询来获取所有已生成网格并拥有渲染边界的区块
                EntityQuery meshedChunksQuery = world.EntityManager.CreateEntityQuery(typeof(Chunk), typeof(TerrainChunkMesh), typeof(WorldRenderBounds));

                Gizmos.color = Color.green;

                // 使用 EntityManager 的 API 从查询中获取组件数据
                using var visibleBounds = meshedChunksQuery.ToComponentDataArray<WorldRenderBounds>(Allocator.Temp);

                foreach (var chunk in visibleBounds)
                {
                    // 严格遵循原框架的绘制API：Center 和 Extents * 2
                    // 并进行必要的类型转换
                    Gizmos.DrawWireCube((Vector3)chunk.Value.Center, (Vector3)chunk.Value.Extents * 2);
                }
            }
        }
    }
}