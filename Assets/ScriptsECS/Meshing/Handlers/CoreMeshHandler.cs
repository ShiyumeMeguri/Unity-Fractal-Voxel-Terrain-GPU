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

        public void Schedule(ref TerrainChunkVoxels chunkVoxels, ref NormalsHandler normals, JobHandle dependency)
        {
            // [修复] 此处不再需要 `ref`，因为 `chunkVoxels.Voxels` 是一个 `NativeArray`，它本身就是引用类型
            JobHandle = VoxelMeshBuilder.ScheduleMeshingJob(chunkVoxels.Voxels, _PaddedChunkSize, MeshData, normals.VoxelNormals, dependency);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            if (MeshData != null) MeshData.Dispose();
        }
    }
}