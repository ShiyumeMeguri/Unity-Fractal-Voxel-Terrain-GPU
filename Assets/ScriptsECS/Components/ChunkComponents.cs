// Assets/ScriptsECS/Components/ChunkComponents.cs

using OptIn.Voxel;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public struct Chunk : IComponentData
{
    public int3 Position;
    public byte SkirtMask;
    public FixedList64Bytes<Entity> Skirts;
    public BitField32 NeighbourMask;
}

public struct TerrainSkirt : IComponentData
{
    public byte Direction;
}

public struct TerrainSkirtLinkedParent : IComponentData
{
    public Entity ChunkParent;
}

public struct ChunkVoxelData : IComponentData, IEnableableComponent, IDisposable
{
    public NativeArray<Voxel> Voxels;
    public JobHandle AsyncWriteJobHandle;
    public JobHandle AsyncReadJobHandle;

    public bool IsCreated => Voxels.IsCreated;

    public ChunkVoxelData(int3 paddedSize, Allocator allocator)
    {
        int count = paddedSize.x * paddedSize.y * paddedSize.z;
        Voxels = new NativeArray<Voxel>(count, allocator, NativeArrayOptions.UninitializedMemory);
        AsyncWriteJobHandle = default;
        AsyncReadJobHandle = default;
    }

    public void Dispose()
    {
        if (IsCreated) Voxels.Dispose();
    }
}

public struct TerrainChunkMesh : IComponentData, IEnableableComponent, IDisposable
{
    public NativeArray<float3> Vertices;
    public NativeArray<int> MainMeshIndices;
    public JobHandle AccessJobHandle;

    public static TerrainChunkMesh FromJobHandler(NativeArray<float3> vertices, NativeArray<int> indices, int vertexCount, int indexCount, JobHandle dependency)
    {
        var newVertices = new NativeArray<float3>(vertexCount, Allocator.Persistent);
        var newIndices = new NativeArray<int>(indexCount, Allocator.Persistent);

        var vHandle = new CopyJob<float3> { Source = vertices.GetSubArray(0, vertexCount), Destination = newVertices }.Schedule(dependency);
        var iHandle = new CopyJob<int> { Source = indices.GetSubArray(0, indexCount), Destination = newIndices }.Schedule(dependency);

        var finalHandle = JobHandle.CombineDependencies(vHandle, iHandle);

        return new TerrainChunkMesh()
        {
            Vertices = newVertices,
            MainMeshIndices = newIndices,
            AccessJobHandle = finalHandle,
        };
    }

    public void Dispose()
    {
        AccessJobHandle.Complete();
        if (Vertices.IsCreated) Vertices.Dispose();
        if (MainMeshIndices.IsCreated) MainMeshIndices.Dispose();
    }

    [BurstCompile]
    private struct CopyJob<T> : IJob where T : struct
    {
        [ReadOnly] public NativeArray<T> Source;
        [WriteOnly] public NativeArray<T> Destination;
        public void Execute() => Source.CopyTo(Destination);
    }
}

public struct TerrainChunkRequestReadbackTag : IComponentData, IEnableableComponent
{
    public bool SkipMeshingIfEmpty;
}

public struct TerrainChunkVoxelsReadyTag : IComponentData, IEnableableComponent { }

public struct TerrainChunkRequestMeshingTag : IComponentData, IEnableableComponent
{
    public bool DeferredVisibility;
}

public struct TerrainChunkRequestCollisionTag : IComponentData, IEnableableComponent { }

public struct TerrainChunkEndOfPipeTag : IComponentData, IEnableableComponent { }

public struct TerrainDeferredVisible : IComponentData, IEnableableComponent { }