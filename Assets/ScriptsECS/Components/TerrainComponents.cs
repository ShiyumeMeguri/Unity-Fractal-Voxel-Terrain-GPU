// Components/TerrainComponents.cs
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// 地形全局配置单例组件
public struct TerrainConfig : IComponentData
{
    public int3 ChunkSize;
    public int3 PaddedChunkSize;
    public int2 ChunkSpawnSize;
}

// 托管资源单例组件
public class TerrainResources : IComponentData
{
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
}

// 网格生成器配置单例组件
public struct TerrainMesherConfig : IComponentData
{
    public int MeshJobsPerTick;
}

// 用于向特定区块应用体素编辑的请求组件
public struct VoxelEditRequest : IComponentData
{
    public enum EditType { SetBlock }

    public EditType Type;
    public float3 WorldPosition;
    public short VoxelID;
}