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

                // 添加TerrainLoader组件，ChunkManagerSystem将查询这个组件来确定区块生成中心
                AddComponent(entity, new TerrainLoader
                {
                    Position = float3.zero, // 将由System在运行时更新
                    LastChunkPosition = new int3(int.MinValue)
                });

                // 保留PlayerTag以便其他系统（如输入）可以识别玩家
                AddComponent<PlayerTag>(entity);
            }
        }
    }
}