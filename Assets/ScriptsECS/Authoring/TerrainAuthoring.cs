using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class TerrainAuthoring : MonoBehaviour
{
    public int3 ChunkSize = new int3(32, 32, 32);
    public int2 ChunkSpawnSize = new int2(8, 8);
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
    public GameObject VoxelControllerPrefab;

    class Baker : Baker<TerrainAuthoring>
    {
        public override void Bake(TerrainAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // 烘焙地形配置为单例组件
            AddComponent(entity, new TerrainConfig
            {
                ChunkSize = authoring.ChunkSize,
                ChunkSpawnSize = authoring.ChunkSpawnSize,
                VoxelControllerPrefab = GetEntity(authoring.VoxelControllerPrefab, TransformUsageFlags.None)
            });

            // 将材质和Compute Shader作为托管组件添加
            AddComponentObject(entity, new TerrainResources
            {
                ChunkMaterial = authoring.ChunkMaterial,
                VoxelComputeShader = authoring.VoxelComputeShader
            });
        }
    }
}

// 使用托管组件来存储对Unity对象的引用
public class TerrainResources : IComponentData
{
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
}