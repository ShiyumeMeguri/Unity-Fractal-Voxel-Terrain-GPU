// Authoring/TerrainAuthoring.cs
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ruri.Voxel
{
    // [新增] 将相关组件定义移至此处，保持逻辑内聚
    public class TerrainResources : IComponentData
    {
        public Material ChunkMaterial;
        public ComputeShader VoxelComputeShader;
    }

    public class TerrainMesherConfig : IComponentData
    {
        public int meshJobsPerTick;
    }

    public struct TerrainReadbackConfig : IComponentData { }


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

                // 1. 烘焙地形核心配置
                AddComponent(entity, new TerrainConfig
                {
                    ChunkSize = authoring.ChunkSize,
                    PaddedChunkSize = authoring.ChunkSize + 2,
                    ChunkSpawnSize = authoring.ChunkSpawnSize
                });

                // 2. 烘焙网格生成器配置
                AddComponentObject(entity, new TerrainMesherConfig
                {
                    meshJobsPerTick = authoring.MeshJobsPerTick
                });

                // 3. 烘焙托管资源
                AddComponentObject(entity, new TerrainResources
                {
                    ChunkMaterial = authoring.ChunkMaterial,
                    VoxelComputeShader = authoring.VoxelComputeShader
                });

                // 4. [修正] 烘焙Readback配置，这是激活TerrainReadbackSystem的关键！
                AddComponent(entity, new TerrainReadbackConfig());
            }
        }
    }
}