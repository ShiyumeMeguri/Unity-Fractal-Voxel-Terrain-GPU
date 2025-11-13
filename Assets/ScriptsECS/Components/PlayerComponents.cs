// Components/PlayerComponents.cs
using Unity.Entities;
using Unity.Mathematics;
using Ruri.Voxel; // [新增] 统一命名空间

namespace Ruri.Voxel
{
    public struct PlayerTag : IComponentData { }

    public struct TerrainLoader : IComponentData
    {
        public float3 Position;
        public int3 LastChunkPosition;
    }
}