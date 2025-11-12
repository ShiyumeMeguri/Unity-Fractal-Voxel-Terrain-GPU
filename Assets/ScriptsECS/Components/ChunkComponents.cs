using OptIn.Voxel;
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// 标识一个实体是区块
public struct Chunk : IComponentData
{
    public int3 Position;
}

// 实现 IDisposable 以便在实体销毁时正确释放 NativeArray
public struct ChunkVoxelData : IComponentData, IDisposable
{
    public NativeArray<Voxel> Voxels;
    public bool IsCreated => Voxels.IsCreated;

    public void Dispose()
    {
        if (IsCreated) Voxels.Dispose();
    }

    public void Dispose(JobHandle dependency)
    {
        if (IsCreated) Voxels.Dispose(dependency);
    }
}

// 托管组件，用于追踪异步GPU数据请求
public class GpuVoxelDataRequest : IComponentData
{
    public AsyncGPUReadbackRequest Request;
    public NativeArray<Voxel> TempVoxelData;
    public ComputeBuffer Buffer;
    // [修复] 修正为单个实体引用，以匹配一对一的请求模式
    public Entity TargetEntity;
}

// 托管组件，用于持有正在进行的网格生成作业的数据和句柄
public class MeshingJobData : IComponentData, IDisposable
{
    public JobHandle JobHandle;
    public VoxelMeshBuilder.NativeMeshData MeshData;

    public void Dispose()
    {
        JobHandle.Complete();
        MeshData?.Dispose();
    }
}

// 托管组件，用于在系统之间传递网格引用
public class GeneratedMesh : IComponentData
{
    public Mesh Mesh;
}

// --- 区块处理管道标签 ---
public struct NewChunkTag : IComponentData { }
public struct RequestGpuDataTag : IComponentData, IEnableableComponent { }
public struct PendingGpuDataTag : IComponentData, IEnableableComponent { }
public struct RequestPaddingUpdateTag : IComponentData, IEnableableComponent { }
public struct RequestMeshTag : IComponentData, IEnableableComponent { }
public struct PendingMeshTag : IComponentData, IEnableableComponent { }
public struct RequestColliderBakeTag : IComponentData, IEnableableComponent { }
public struct ChunkModifiedTag : IComponentData, IEnableableComponent { }
public struct IdleTag : IComponentData, IEnableableComponent { }