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
        private int3 m_PaddedChunkSize;

        public void Init(TerrainConfig config)
        {
            m_PaddedChunkSize = config.PaddedChunkSize;
            MeshData = new VoxelMeshBuilder.NativeMeshData(m_PaddedChunkSize);
        }
        
        // [修复] 接收NormalsHandler并将其法线数据传递下去
        public void Schedule(ref TerrainChunkVoxels chunkVoxels, ref NormalsHandler normals, JobHandle dependency)
        {
            JobHandle = VoxelMeshBuilder.ScheduleMeshingJob(chunkVoxels.Voxels, m_PaddedChunkSize, MeshData, normals.VoxelNormals, dependency);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            MeshData.Dispose();
        }
    }
}