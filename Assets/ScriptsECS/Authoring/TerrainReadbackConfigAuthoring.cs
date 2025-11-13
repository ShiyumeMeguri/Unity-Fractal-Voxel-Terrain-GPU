using Unity.Entities;
using UnityEngine;

namespace Ruri.Voxel
{
    class TerrainReadbackConfigAuthoring : MonoBehaviour {
    }

    class TerrainReadbackConfigBaker : Baker<TerrainReadbackConfigAuthoring> {
        public override void Bake(TerrainReadbackConfigAuthoring authoring) {
            Entity self = GetEntity(TransformUsageFlags.None);

            AddComponent(self, new TerrainReadbackConfig {
            });
        }
    }
}