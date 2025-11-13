// Components/ChunkComponents.cs
using OptIn.Voxel.Meshing;
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

public struct TerrainChunkVoxels : IComponentData, IEnableableComponent
{
    public NativeArray<OptIn.Voxel.VoxelData> Voxels;
    public JobHandle AsyncWriteJobHandle;
    public JobHandle AsyncReadJobHandle;
    public bool MeshingInProgress;

    public bool IsCreated => Voxels.IsCreated;

    public void Dispose(JobHandle dependency)
    {
        if (IsCreated)
        {
            var disposeHandle = JobHandle.CombineDependencies(AsyncWriteJobHandle, AsyncReadJobHandle, dependency);
            Voxels.Dispose(disposeHandle);
        }
    }
}


public struct TerrainChunkMesh : IComponentData, IEnableableComponent
{
    public NativeArray<float3> Vertices;
    public NativeArray<float3> Normals;
    public NativeArray<int> MainMeshIndices;
    public JobHandle AccessJobHandle;

    public static TerrainChunkMesh FromJobHandlerStats(MeshJobHandler.Stats stats)
    {
        var vertices = new NativeArray<float3>(stats.VertexCount, Allocator.Persistent);
        var normals = new NativeArray<float3>(stats.VertexCount, Allocator.Persistent);
        var indices = new NativeArray<int>(stats.MainMeshIndexCount, Allocator.Persistent);

        if (stats.VertexCount > 0)
        {
            stats.Vertices.positions.GetSubArray(0, stats.VertexCount).CopyTo(vertices);
            stats.Vertices.normals.GetSubArray(0, stats.VertexCount).CopyTo(normals);
        }
        if (stats.MainMeshIndexCount > 0)
        {
            stats.MainMeshIndices.GetSubArray(0, stats.MainMeshIndexCount).CopyTo(indices);
        }

        return new TerrainChunkMesh()
        {
            Vertices = vertices,
            Normals = normals,
            MainMeshIndices = indices,
        };
    }

    public void Dispose()
    {
        AccessJobHandle.Complete();
        if (Vertices.IsCreated) Vertices.Dispose();
        if (Normals.IsCreated) Normals.Dispose();
        if (MainMeshIndices.IsCreated) MainMeshIndices.Dispose();
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