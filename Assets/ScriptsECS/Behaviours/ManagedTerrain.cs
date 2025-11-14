using UnityEngine;

namespace Ruri.Voxel
{
    public class ManagedTerrain : MonoBehaviour
    {
        public static ManagedTerrain instance;

        // [修正] 暂时移除对Compiler和Graph的引用，因为我们简化了流程
        // public ManagedTerrainCompiler compiler;

        void Awake()
        {
            instance = this;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            instance = null;
        }
    }
}