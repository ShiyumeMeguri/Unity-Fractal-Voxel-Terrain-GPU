// Components/TerrainReadySystems.cs
using Unity.Entities;

namespace Ruri.Voxel
{
    public struct TerrainReadySystems : IComponentData
    {
        public bool manager;
        public bool readback;
        public bool mesher;
    }
}