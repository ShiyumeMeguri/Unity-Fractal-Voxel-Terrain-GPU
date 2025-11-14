using Unity.Entities;
using UnityEngine.Scripting;

namespace Ruri.Voxel
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(FixedStepSimulationSystemGroup))]
    public partial class TerrainFixedStepSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public TerrainFixedStepSystemGroup()
        {
            // 设置一个固定的更新频率，例如16ms (~60 FPS)
            uint msBetweenTicks = 16;
            SetRateManagerCreateAllocator(new RateUtils.VariableRateManager(msBetweenTicks));
        }
    }
}