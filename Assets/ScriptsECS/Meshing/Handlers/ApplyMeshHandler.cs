using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Ruri.Voxel
{
    internal struct ApplyMeshHandler : ISubHandler
    {
        public NativeReference<MinMaxAABB> Bounds;
        public JobHandle JobHandle;
        public Mesh.MeshDataArray MeshDataArray;

        public void Init(TerrainConfig config)
        {
            Bounds = new NativeReference<MinMaxAABB>(Allocator.Persistent);
        }

        public void Schedule(ref MergeMeshHandler merger)
        {
            Bounds.Value = new MinMaxAABB { Min = new float3(float.MaxValue), Max = new float3(float.MinValue) };
            MeshDataArray = Mesh.AllocateWritableMeshData(1);

            var boundsJob = new BoundsJob
            {
                MergedVerticesPositions = merger.MergedVertices.positions,
                TotalVertexCount = merger.TotalVertexCount,
                Bounds = Bounds
            };

            var setMeshDataJob = new SetMeshDataJob
            {
                Data = MeshDataArray[0],
                MergedVertices = merger.MergedVertices,
                MergedIndices = merger.MergedIndices,
                SubmeshIndexOffsets = merger.SubmeshIndexOffsets,
                SubmeshIndexCounts = merger.SubmeshIndexCounts,
                TotalIndexCount = merger.TotalIndexCount,
                TotalVertexCount = merger.TotalVertexCount,
            };

            var boundsHandle = boundsJob.Schedule(merger.JobHandle);
            var setDataHandle = setMeshDataJob.Schedule(merger.JobHandle);
            JobHandle = JobHandle.CombineDependencies(boundsHandle, setDataHandle);
        }

        public void Dispose()
        {
            JobHandle.Complete();
            if (Bounds.IsCreated) Bounds.Dispose();
        }
    }
}