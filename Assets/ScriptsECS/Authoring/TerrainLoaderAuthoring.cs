// Authoring/TerrainLoaderAuthoring.cs
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ruri.Voxel
{
    public class TerrainLoaderAuthoring : MonoBehaviour
    {
        class Baker : Baker<TerrainLoaderAuthoring>
        {
            public override void Bake(TerrainLoaderAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new TerrainLoader
                {
                    Position = float3.zero,
                    LastChunkPosition = new int3(int.MinValue) // 初始化为一个不可能的值
                });

                AddComponent<PlayerTag>(entity);
            }
        }
    }
}