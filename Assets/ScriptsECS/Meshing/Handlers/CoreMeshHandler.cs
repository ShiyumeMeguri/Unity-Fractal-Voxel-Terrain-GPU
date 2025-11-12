// Assets/ScriptsECS/Meshing/Handlers/CoreMeshHandler.cs
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    internal struct CoreMeshHandler : ISubHandler
    {
        // [修复] 移除 VoxelMeshBuilder.NativeMeshData, 将其成员直接放在这里
        public NativeArray<GPUVertex> nativeVertices;
        public NativeArray<int> nativeIndices;
        public NativeArray<int> vertexIndices;
        public NativeCounter counter;

        public JobHandle JobHandle;
        private int3 _PaddedChunkSize;

        public void Init(TerrainConfig config)
        {
            _PaddedChunkSize = config.PaddedChunkSize;
            int numVoxels = _PaddedChunkSize.x * _PaddedChunkSize.y * _PaddedChunkSize.z;
            int maxVertices = numVoxels * 12;
            int maxIndices = maxVertices * 2;

            nativeVertices = new NativeArray<GPUVertex>(maxVertices, Allocator.Persistent);
            nativeIndices = new NativeArray<int>(maxIndices, Allocator.Persistent);
            vertexIndices = new NativeArray<int>(numVoxels, Allocator.Persistent);
            counter = new NativeCounter(Allocator.Persistent);
        }

        public void Schedule(ref TerrainChunkVoxels chunkVoxels, ref NormalsHandler normals, JobHandle dependency)
        {
            // [修复] 直接传递 NativeArray 和 NativeCounter
            JobHandle = VoxelMeshBuilder.ScheduleMeshingJob(
                chunkVoxels.Voxels,
                _PaddedChunkSize,
                nativeVertices,
                nativeIndices,
                counter,
                normals.VoxelNormals,
                dependency);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            // [修复] 直接释放成员
            if (nativeVertices.IsCreated) nativeVertices.Dispose();
            if (nativeIndices.IsCreated) nativeIndices.Dispose();
            if (vertexIndices.IsCreated) vertexIndices.Dispose();
            if (counter.IsCreated) counter.Dispose();
        }
    }
}