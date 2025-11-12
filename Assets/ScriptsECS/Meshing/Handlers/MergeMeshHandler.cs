// Assets/ScriptsECS/Meshing/Handlers/MergeMeshHandler.cs
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OptIn.Voxel.Meshing
{
    internal struct MergeMeshHandler : ISubHandler
    {
        public Vertices MergedVertices;
        public NativeArray<int> MergedIndices;

        public NativeArray<int> SubmeshIndexOffsets;
        public NativeArray<int> SubmeshIndexCounts;
        public NativeReference<int> TotalVertexCount;
        public NativeReference<int> TotalIndexCount;

        public JobHandle JobHandle;

        public void Init(TerrainConfig config)
        {
            int totalMaxVertices = config.PaddedChunkSize.x * config.PaddedChunkSize.y * config.PaddedChunkSize.z * 12;
            int totalMaxIndices = totalMaxVertices / 4 * 6 * 2;

            MergedVertices = new Vertices(totalMaxVertices, Allocator.Persistent);
            MergedIndices = new NativeArray<int>(totalMaxIndices, Allocator.Persistent);

            SubmeshIndexOffsets = new NativeArray<int>(7, Allocator.Persistent);
            SubmeshIndexCounts = new NativeArray<int>(7, Allocator.Persistent);
            TotalVertexCount = new NativeReference<int>(Allocator.Persistent);
            TotalIndexCount = new NativeReference<int>(Allocator.Persistent);
        }

        public void Schedule(ref CoreMeshHandler core, ref SkirtHandler skirt)
        {
            TotalVertexCount.Value = 0;
            TotalIndexCount.Value = 0;

            var job = new MergeMeshJob
            {
                // [修复] 直接访问 core 的成员
                Vertices = core.nativeVertices,
                Indices = core.nativeIndices,
                VertexCounter = core.counter,
                TriangleCounter = core.counter,

                SkirtVertices = skirt.SkirtVertices,
                SkirtStitchedIndices = skirt.StitchedIndices,
                SkirtForcedPerFaceIndices = skirt.ForcedPerFaceIndices,
                SkirtVertexCounter = skirt.VertexCounter,
                SkirtStitchedTriangleCounter = skirt.StitchedTriangleCounter,
                SkirtForcedTriangleCounter = skirt.ForcedTriangleCounter,

                SubmeshIndexOffsets = SubmeshIndexOffsets,
                SubmeshIndexCounts = SubmeshIndexCounts,
                TotalVertexCount = TotalVertexCount,
                TotalIndexCount = TotalIndexCount,

                MergedVertices = MergedVertices,
                MergedIndices = MergedIndices
            };

            JobHandle = job.Schedule(JobHandle.CombineDependencies(core.JobHandle, skirt.JobHandle));
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