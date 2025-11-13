// Components/TerrainComponents.cs
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Ruri.Voxel; // [新增] 统一命名空间

namespace Ruri.Voxel
{
    public struct TerrainConfig : IComponentData
    {
        public int3 ChunkSize;
        public int3 PaddedChunkSize;
        public int2 ChunkSpawnSize;
    }

    public class TerrainResources : IComponentData
    {
        public Material ChunkMaterial;
        public ComputeShader VoxelComputeShader;
    }

    public struct TerrainMesherConfig : IComponentData
    {
        public int MeshJobsPerTick;
    }

    public struct VoxelEditRequest : IComponentData
    {
        public enum EditType { SetBlock, ModifySphere } // [修正] 遵循OOP中的ModifySphere功能

        public EditType Type;
        public float3 WorldPosition;
        public float Radius; // 用于球体编辑
        public float Intensity; // 用于球体编辑
        public short VoxelID; // 对于SetBlock是方块ID，对于ModifySphere是材质ID
    }
}