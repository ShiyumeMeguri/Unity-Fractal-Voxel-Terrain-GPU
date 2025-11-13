// Authoring/TerrainAuthoring.cs
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class TerrainAuthoring : MonoBehaviour
{
    public int3 ChunkSize = new int3(32, 32, 32);
    public int2 ChunkSpawnSize = new int2(8, 8);
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
    public int MeshJobsPerTick = 4;

    class Baker : Baker<TerrainAuthoring>
    {
        public override void Bake(TerrainAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // 1. 烘焙地形配置到ECS单例组件
            AddComponent(entity, new TerrainConfig
            {
                ChunkSize = authoring.ChunkSize,
                PaddedChunkSize = authoring.ChunkSize + 2, // 直接在这里计算
                ChunkSpawnSize = authoring.ChunkSpawnSize,
            });

            // 2. 烘焙网格生成器配置
            AddComponent(entity, new TerrainMesherConfig
            {
                MeshJobsPerTick = authoring.MeshJobsPerTick
            });

            // 3. 烘焙托管资源（材质、ComputeShader）
            AddComponentObject(entity, new TerrainResources
            {
                ChunkMaterial = authoring.ChunkMaterial,
                VoxelComputeShader = authoring.VoxelComputeShader
            });

            // 4. 为ManagedTerrain单例关联烘焙好的资源
            // 注意：这是一种在ECS中与MonoBehaviour交互的模式
            var managedTerrain = FindObjectOfType<ManagedTerrain>();
            if (managedTerrain != null)
            {
                managedTerrain.SetResources(new TerrainResources
                {
                    ChunkMaterial = authoring.ChunkMaterial,
                    VoxelComputeShader = authoring.VoxelComputeShader
                });
            }
        }
    }
}

// 将组件定义移到单独的文件中会更清晰，但为了遵循你的结构，暂时放在这里
// 这些是被烘焙进ECS世界的数据
public class TerrainResources : IComponentData
{
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
}

public struct TerrainMesherConfig : IComponentData
{
    public int MeshJobsPerTick;
}