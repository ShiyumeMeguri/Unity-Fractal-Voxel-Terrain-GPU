// Components/PlayerComponents.cs
using Unity.Entities;
using Unity.Mathematics;

// 用于识别玩家/加载器实体
public struct PlayerTag : IComponentData { }

// 用于驱动地形生成的加载器，包含其位置信息
public struct TerrainLoader : IComponentData
{
    public float3 Position;
    public int3 LastChunkPosition;
}