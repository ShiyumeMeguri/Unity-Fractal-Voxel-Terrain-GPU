using OptIn.Voxel;

namespace OptIn.Voxel.Meshing
{
    public interface ISubHandler
    {
        public void Init(TerrainConfig config);
        public void Dispose();
    }
}