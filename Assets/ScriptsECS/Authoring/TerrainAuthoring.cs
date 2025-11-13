// Authoring/TerrainAuthoring.cs
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Ruri.Voxel; // [新增] 统一命名空间

namespace Ruri.Voxel
{
    // [修正] 这是场景中唯一的配置入口，类似原框架的 Voxel Terrain GameObject
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

                // 1. 烘焙地形核心配置，所有系统都将读取这个单例
                AddComponent(entity, new TerrainConfig
                {
                    ChunkSize = authoring.ChunkSize,
                    PaddedChunkSize = authoring.ChunkSize + 2, // 直接在这里计算Padding后的大小
                    ChunkSpawnSize = authoring.ChunkSpawnSize,
                });

                // 2. 烘焙网格生成器配置
                AddComponent(entity, new TerrainMesherConfig
                {
                    MeshJobsPerTick = authoring.MeshJobsPerTick
                });

                // 3. 烘焙托管资源（材质、ComputeShader），这些不能放在struct中
                AddComponentObject(entity, new TerrainResources
                {
                    ChunkMaterial = authoring.ChunkMaterial,
                    VoxelComputeShader = authoring.VoxelComputeShader
                });
            }
        }
    }
}