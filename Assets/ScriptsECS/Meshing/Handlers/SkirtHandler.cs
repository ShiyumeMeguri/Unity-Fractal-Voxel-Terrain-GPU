// Assets/ScriptsECS/Meshing/Handlers/SkirtHandler.cs
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
        private int3 _PaddedChunkSize;

        public void Init(TerrainConfig config)
        {
            _PaddedChunkSize = config.PaddedChunkSize;
            int faceArea = _PaddedChunkSize.x * _PaddedChunkSize.y;
            int skirtFaceArea = faceArea;

            WithinThreshold = new NativeArray<bool>(faceArea * 6, Allocator.Persistent);
            CopiedVertexIndices = new NativeArray<int>(faceArea * 6, Allocator.Persistent);
            GeneratedVertexIndices = new NativeArray<int>(skirtFaceArea * 6, Allocator.Persistent);
            StitchedIndices = new NativeArray<int>(skirtFaceArea * 2 * 6 * 6, Allocator.Persistent);
            ForcedPerFaceIndices = new NativeArray<int>(skirtFaceArea * 6 * 6, Allocator.Persistent);

            SkirtVertices = new Vertices(skirtFaceArea * 6, Allocator.Persistent);
            VertexCounter = new NativeCounter(Allocator.Persistent);
            StitchedTriangleCounter = new NativeCounter(Allocator.Persistent);
            ForcedTriangleCounter = new NativeMultiCounter(6, Allocator.Persistent);
        }

        public void Schedule(ref TerrainChunkVoxels voxels, ref CoreMeshHandler core, ref NormalsHandler normals, JobHandle dependency)
        {
            VertexCounter.Count = 0;
            StitchedTriangleCounter.Count = 0;
            ForcedTriangleCounter.Reset();

            var closestSurfaceJob = new SkirtClosestSurfaceJob
            {
                Voxels = voxels.Voxels,
                WithinThreshold = WithinThreshold,
                PaddedChunkSize = _PaddedChunkSize
            };
            var closestHandle = closestSurfaceJob.Schedule(FACE * 6, 64, dependency);

            var copyJob = new SkirtCopyVertexIndicesJob
            {
                // [修复] 直接访问 core.vertexIndices
                SourceVertexIndices = core.vertexIndices,
                SkirtVertexIndicesCopied = CopiedVertexIndices,
                PaddedChunkSize = _PaddedChunkSize
            };
            var copyHandle = copyJob.Schedule(core.JobHandle);

            var vertexJob = new SkirtVertexJob
            {
                Voxels = voxels.Voxels,
                WithinThreshold = WithinThreshold,
                SkirtVertexIndicesGenerated = GeneratedVertexIndices,
                SkirtVertices = SkirtVertices,
                SkirtVertexCounter = VertexCounter.ToConcurrent(),
                // [修复] 直接访问 core.counter
                VertexCounter = core.counter,
                PaddedChunkSize = _PaddedChunkSize,
                voxelNormals = normals.VoxelNormals
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
                PaddedChunkSize = _PaddedChunkSize
            };
            JobHandle = quadJob.Schedule(FACE * 6, 64, vertexHandle);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            if (WithinThreshold.IsCreated) WithinThreshold.Dispose();
            if (CopiedVertexIndices.IsCreated) CopiedVertexIndices.Dispose();
            if (GeneratedVertexIndices.IsCreated) GeneratedVertexIndices.Dispose();
            if (StitchedIndices.IsCreated) StitchedIndices.Dispose();
            if (ForcedPerFaceIndices.IsCreated) ForcedPerFaceIndices.Dispose();
            SkirtVertices.Dispose();
            if (VertexCounter.IsCreated) VertexCounter.Dispose();
            if (StitchedTriangleCounter.IsCreated) StitchedTriangleCounter.Dispose();
            if (ForcedTriangleCounter.IsCreated) ForcedTriangleCounter.Dispose();
        }
    }
}