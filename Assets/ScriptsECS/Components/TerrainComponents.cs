// Components/TerrainComponents.cs
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ruri.Voxel
{
    public struct TerrainConfig : IComponentData
    {
        public int3 ChunkSize;
        public int3 PaddedChunkSize;
        public int2 ChunkSpawnSize;
    }

    public struct VoxelEditRequest : IComponentData
    {
        public enum EditType { SetBlock, ModifySphere }

        public EditType Type;
        public float3 WorldPosition;
        public float Radius;
        public float Intensity;
        public short VoxelID;
    }
}