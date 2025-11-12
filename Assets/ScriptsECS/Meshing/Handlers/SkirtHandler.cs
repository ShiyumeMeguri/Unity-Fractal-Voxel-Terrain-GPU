// Assets/ScriptsECS/Meshing/SkirtHandler.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static OptIn.Voxel.VoxelUtils;

namespace OptIn.Voxel.Meshing
{
    internal struct SkirtHandler : ISubHandler
    {
        public Vertices SkirtVertices;
        public NativeCounter VertexCounter;

        public NativeArray<bool> WithinThreshold;
        public NativeArray<int> CopiedVertexIndices;
        public NativeArray<int> GeneratedVertexIndices;
        public NativeArray<int> StitchedIndices;
        public NativeArray<int> ForcedPerFaceIndices;

        public NativeCounter StitchedTriangleCounter;
        public NativeMultiCounter ForcedTriangleCounter;

        public JobHandle JobHandle;
        private int3 m_PaddedChunkSize; // 新增字段

        public void Init(TerrainConfig config)
        {
            m_PaddedChunkSize = config.PaddedChunkSize; // 存储尺寸

            WithinThreshold = new NativeArray<bool>(FACE * 6, Allocator.Persistent);
            CopiedVertexIndices = new NativeArray<int>(FACE * 6, Allocator.Persistent);
            GeneratedVertexIndices = new NativeArray<int>(SKIRT_FACE * 6, Allocator.Persistent);
            StitchedIndices = new NativeArray<int>(SKIRT_FACE * 2 * 6 * 6, Allocator.Persistent);
            ForcedPerFaceIndices = new NativeArray<int>(SKIRT_FACE * 6 * 6, Allocator.Persistent);

            SkirtVertices = new Vertices(SKIRT_FACE * 6, Allocator.Persistent);
            VertexCounter = new NativeCounter(Allocator.Persistent);
            StitchedTriangleCounter = new NativeCounter(Allocator.Persistent);
            ForcedTriangleCounter = new NativeMultiCounter(6, Allocator.Persistent);
        }

        public void Schedule(ref ChunkVoxelData voxels, ref CoreMeshHandler core, JobHandle dependency)
        {
            VertexCounter.Count = 0;
            StitchedTriangleCounter.Count = 0;
            ForcedTriangleCounter.Reset();

            var closestSurfaceJob = new SkirtClosestSurfaceJob
            {
                Voxels = voxels.Voxels,
                WithinThreshold = WithinThreshold,
                PaddedChunkSize = m_PaddedChunkSize // 传递尺寸
            };
            var closestHandle = closestSurfaceJob.Schedule(FACE * 6, 64, dependency);

            var copyJob = new SkirtCopyVertexIndicesJob
            {
                SourceVertexIndices = core.MeshData.vertexIndices,
                SkirtVertexIndicesCopied = CopiedVertexIndices,
                PaddedChunkSize = m_PaddedChunkSize // 传递尺寸
            };
            var copyHandle = copyJob.Schedule(core.JobHandle);

            var vertexJob = new SkirtVertexJob
            {
                Voxels = voxels.Voxels,
                WithinThreshold = WithinThreshold,
                SkirtVertexIndicesGenerated = GeneratedVertexIndices,
                SkirtVertices = SkirtVertices,
                SkirtVertexCounter = VertexCounter.ToConcurrent(),
                VertexCounter = core.MeshData.counter,
                PaddedChunkSize = m_PaddedChunkSize // 传递尺寸
            };
            var vertexHandle = vertexJob.Schedule(SKIRT_FACE * 6, 64, JobHandle.CombineDependencies(copyHandle, closestHandle));

            var quadJob = new SkirtQuadJob
            {
                Voxels = voxels.Voxels,
                SkirtVertexIndicesCopied = CopiedVertexIndices,
                SkirtVertexIndicesGenerated = GeneratedVertexIndices,
                SkirtStitchedIndices = StitchedIndices,
                SkirtForcedPerFaceIndices = ForcedPerFaceIndices,
                SkirtStitchedTriangleCounter = StitchedTriangleCounter.ToConcurrent(),
                SkirtForcedTriangleCounter = ForcedTriangleCounter.ToConcurrent(),
                PaddedChunkSize = m_PaddedChunkSize // 传递尺寸
            };
            JobHandle = quadJob.Schedule(FACE * 6, 64, vertexHandle);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            WithinThreshold.Dispose();
            CopiedVertexIndices.Dispose();
            GeneratedVertexIndices.Dispose();
            StitchedIndices.Dispose();
            ForcedPerFaceIndices.Dispose();
            SkirtVertices.Dispose();
            VertexCounter.Dispose();
            StitchedTriangleCounter.Dispose();
            ForcedTriangleCounter.Dispose();
        }
    }
}