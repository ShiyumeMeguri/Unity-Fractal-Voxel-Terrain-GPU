using UnityEngine;

namespace Ruri.Voxel
{
    public class ManagedTerrain : MonoBehaviour {
        public static ManagedTerrain instance;

        void Awake() {
            instance = this;
        }

        void Start() {
            instance = this;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            instance = null;
        }
    }
}