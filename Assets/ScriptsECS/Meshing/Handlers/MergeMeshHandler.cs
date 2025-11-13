// Assets/ScriptsECS/Meshing/Handlers/MergeMeshHandler.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ruri.Voxel
{
    internal struct MergeMeshHandler : ISubHandler
    {
        public Vertices MergedVertices;
        public NativeArray<int> MergedIndices;

        // REFACTOR: Submesh data is simplified as we no longer have skirts.
        public NativeArray<int> SubmeshIndexOffsets;
        public NativeArray<int> SubmeshIndexCounts;
        public NativeReference<int> TotalVertexCount;
        public NativeReference<int> TotalIndexCount;

        public JobHandle JobHandle;

        public void Init(TerrainConfig config)
        {
            // Allocate enough memory for the worst case scenario of a single mesh.
            int maxVertices = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z * 12;
            int maxIndices = maxVertices * 2;

            MergedVertices = new Vertices(maxVertices, Allocator.Persistent);
            MergedIndices = new NativeArray<int>(maxIndices, Allocator.Persistent);

            // We still keep the structure for submeshes, but will only use the first one.
            SubmeshIndexOffsets = new NativeArray<int>(7, Allocator.Persistent);
            SubmeshIndexCounts = new NativeArray<int>(7, Allocator.Persistent);
            TotalVertexCount = new NativeReference<int>(Allocator.Persistent);
            TotalIndexCount = new NativeReference<int>(Allocator.Persistent);
        }

        // REFACTOR: Schedule method no longer takes SkirtHandler as a dependency.
        public void Schedule(ref CoreMeshHandler core)
        {
            TotalVertexCount.Value = 0;
            TotalIndexCount.Value = 0;

            var job = new MergeMeshJob
            {
                // Core mesh data
                Vertices = core.Vertices,
                Indices = core.Indices,
                VertexCounter = core.VertexCounter,
                TriangleCounter = core.TriangleCounter,

                // REFACTOR: All skirt-related inputs are removed.

                // Output arrays
                SubmeshIndexOffsets = SubmeshIndexOffsets,
                SubmeshIndexCounts = SubmeshIndexCounts,
                TotalVertexCount = TotalVertexCount,
                TotalIndexCount = TotalIndexCount,
                MergedVertices = MergedVertices,
                MergedIndices = MergedIndices
            };

            // This job now only depends on the core meshing job.
            JobHandle = job.Schedule(core.JobHandle);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            MergedVertices.Dispose();
            MergedIndices.Dispose();
            SubmeshIndexOffsets.Dispose();
            SubmeshIndexCounts.Dispose();
            TotalVertexCount.Dispose();
            TotalIndexCount.Dispose();
        }
    }

}