using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class TerrainAuthoring : MonoBehaviour
{
    public int3 ChunkSize = new int3(32, 32, 32);
    public int2 ChunkSpawnSize = new int2(8, 8);
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;

    class Baker : Baker<TerrainAuthoring>
    {
        public override void Bake(TerrainAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new TerrainConfig
            {
                ChunkSize = authoring.ChunkSize,
                ChunkSpawnSize = authoring.ChunkSpawnSize,
            });

            // 将托管对象存入一个单独的托管组件中
            AddComponentObject(entity, new TerrainResources
            {
                ChunkMaterial = authoring.ChunkMaterial,
                VoxelComputeShader = authoring.VoxelComputeShader
            });
        }
    }
}

// 这是一个托管组件，用于存放无法进入 Burst 编译的引用类型资源
public class TerrainResources : IComponentData
{
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
}