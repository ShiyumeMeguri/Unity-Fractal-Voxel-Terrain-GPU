using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// 地形全局配置单例组件
public struct TerrainConfig : IComponentData
{
    public int3 ChunkSize;
    public int2 ChunkSpawnSize;
    public Entity VoxelControllerPrefab; // 用于烘焙VoxelController设置
}

// VoxelController的ECS版本，用于存储编辑参数
public struct VoxelEditor : IComponentData
{
    public short BlockMaterialId;
    public float SmoothEditRadius;
    public float SmoothEditIntensity;
    public short SmoothMaterialId;
}

// 用于向特定区块应用体素编辑的请求组件
public struct VoxelEditRequest : IComponentData
{
    public enum EditType { SetBlock, ModifySphereAdd, ModifySphereSubtract }

    public EditType Type;
    public float3 Center; // 世界坐标
    
    // For SetBlock
    public short VoxelID;
    
    // For ModifySphere
    public float Radius;
    public float Intensity;
    public short MaterialId;
}