using Unity.Entities;
using Unity.Transforms;

// 标识玩家实体
public struct PlayerTag : IComponentData { }

// 存储来自VoxelController的输入
public struct PlayerInput : IComponentData
{
    public bool PlaceBlock;
    public bool RemoveBlock;
    public bool AddSmooth;
    public bool SubtractSmooth;
}