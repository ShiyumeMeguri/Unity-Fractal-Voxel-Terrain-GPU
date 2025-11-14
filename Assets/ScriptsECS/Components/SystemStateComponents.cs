using Unity.Entities;
using Unity.Mathematics;

namespace Ruri.Voxel
{
    /// <summary>
    /// 单例组件，用于跟踪各个核心地形系统是否准备就绪。
    /// </summary>
    public struct TerrainReadySystems : IComponentData
    {
        public bool manager;
        public bool readback;
        public bool mesher;
    }

    /// <summary>
    /// 单例组件，存储地形生成所需的随机种子。
    /// </summary>
    public struct TerrainSeed : IComponentData
    {
        public int3 permutationSeed;
        public int3 moduloSeed;
        public int seed;
        public bool dirty;
    }
}