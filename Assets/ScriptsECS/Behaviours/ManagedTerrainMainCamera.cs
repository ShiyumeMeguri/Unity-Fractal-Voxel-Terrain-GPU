using UnityEngine;

namespace Ruri.Voxel
{
    public class ManagedTerrainMainCamera : MonoBehaviour {
        public static ManagedTerrainMainCamera instance;
        void Awake() {
            instance = this;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            instance = null;
        }
    }
}