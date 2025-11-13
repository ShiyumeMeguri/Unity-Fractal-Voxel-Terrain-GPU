// Assets/ScriptsECS/Meshing/Handlers/CoreMeshHandler.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ruri.Voxel
{
    internal struct CoreMeshHandler : ISubHandler
    {
        public Vertices Vertices;
        public NativeArray<int> Indices;
        public NativeArray<int> VertexIndices;
        public NativeCounter VertexCounter;
        public NativeCounter TriangleCounter;

        private NativeArray<byte> _enabled;
        private NativeArray<uint> _bits;
        private int _volume;
        private int3 _paddedChunkSize;

        public JobHandle JobHandle;

        public void Init(TerrainConfig config)
        {
            _paddedChunkSize = config.PaddedChunkSize;
            _volume = _paddedChunkSize.x * _paddedChunkSize.y * _paddedChunkSize.z;
            int packedCount = (_volume + 31) / 32;

            Vertices = new Vertices(_volume, Allocator.Persistent);
            Indices = new NativeArray<int>(_volume * 6, Allocator.Persistent);
            VertexIndices = new NativeArray<int>(_volume, Allocator.Persistent);
            VertexCounter = new NativeCounter(Allocator.Persistent);
            TriangleCounter = new NativeCounter(Allocator.Persistent);

            _enabled = new NativeArray<byte>(_volume, Allocator.Persistent);
            _bits = new NativeArray<uint>(packedCount, Allocator.Persistent);
        }

        public void Schedule(ref TerrainChunkVoxels chunkVoxels, ref NormalsHandler normals, JobHandle dependency)
        {
            // [修复] 确保新的 Job 链等待上一个使用此 Handler 的 Job 链完成，并通过链式调用合并多个依赖
            var handle1 = JobHandle.CombineDependencies(this.JobHandle, dependency);
            var handle2 = JobHandle.CombineDependencies(chunkVoxels.AsyncWriteJobHandle, chunkVoxels.AsyncReadJobHandle);
            var combinedDep = JobHandle.CombineDependencies(handle1, handle2);

            VertexCounter.Count = 0;
            TriangleCounter.Count = 0;

            var checkJob = new CheckJob
            {
                Densities = chunkVoxels.Voxels,
                Bits = _bits
            };
            var checkHandle = checkJob.Schedule(_bits.Length, 64, combinedDep);

            var cornerJob = new CornerJob
            {
                Bits = _bits,
                Enabled = _enabled,
                ChunkSize = _paddedChunkSize
            };
            var cornerHandle = cornerJob.Schedule(_volume, 256, checkHandle);

            var vertexJob = new VertexJob
            {
                Voxels = chunkVoxels.Voxels,
                VoxelNormals = normals.VoxelNormals,
                Enabled = _enabled,
                Indices = VertexIndices,
                Vertices = this.Vertices,
                VertexCounter = this.VertexCounter.ToConcurrent(),
                ChunkSize = _paddedChunkSize
            };
            var vertexHandle = vertexJob.Schedule(_volume, 256, cornerHandle);

            var quadJob = new QuadJob
            {
                Voxels = chunkVoxels.Voxels,
                VertexIndices = VertexIndices,
                Enabled = _enabled,
                Triangles = this.Indices,
                TriangleCounter = this.TriangleCounter.ToConcurrent(),
                ChunkSize = _paddedChunkSize
            };
            JobHandle = quadJob.Schedule(_volume, 256, vertexHandle);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            Vertices.Dispose();
            Indices.Dispose();
            VertexIndices.Dispose();
            VertexCounter.Dispose();
            TriangleCounter.Dispose();
            _enabled.Dispose();
            _bits.Dispose();
        }
    }
}