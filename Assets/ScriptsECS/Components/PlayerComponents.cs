// Components/PlayerComponents.cs
using Unity.Entities;
using Unity.Mathematics;

namespace Ruri.Voxel
{
    public struct PlayerTag : IComponentData { }

    public struct TerrainLoader : IComponentData
    {
        public float3 Position;
        public int3 LastChunkPosition;
    }
}