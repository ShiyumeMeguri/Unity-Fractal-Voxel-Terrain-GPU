using Ruri.Voxel;

namespace Ruri.Voxel
{
    public interface ISubHandler
    {
        public void Init(TerrainConfig config);
        public void Dispose();
    }
}