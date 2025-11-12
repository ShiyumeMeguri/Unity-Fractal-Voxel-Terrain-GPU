// Assets/ScriptsECS/Meshing/Handlers/CoreMeshHandler.cs
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    internal struct CoreMeshHandler : ISubHandler
    {
        public VoxelMeshBuilder.NativeMeshData MeshData;
        public JobHandle JobHandle;
        private int3 _PaddedChunkSize;

        public void Init(TerrainConfig config)
        {
            _PaddedChunkSize = config.PaddedChunkSize;
            MeshData = new VoxelMeshBuilder.NativeMeshData(_PaddedChunkSize);
        }
        
        // [修复] 接收NormalsHandler并将其法线数据传递下去
        public void Schedule(ref TerrainChunkVoxels chunkVoxels, ref NormalsHandler normals, JobHandle dependency)
        {
            JobHandle = VoxelMeshBuilder.ScheduleMeshingJob(chunkVoxels.Voxels, _PaddedChunkSize, MeshData, normals.VoxelNormals, dependency);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            MeshData.Dispose();
        }
    }
}