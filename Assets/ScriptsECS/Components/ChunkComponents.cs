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

// [修改] 恢复为包含 NativeArray 的 IComponentData
// 注意：这个组件在添加时，其 NativeArray 必须是未创建的（default）
public struct ChunkVoxelData : IComponentData, IDisposable
{
    public NativeArray<Voxel> Voxels;

    public bool IsCreated => Voxels.IsCreated;

    public void Dispose()
    {
        if (IsCreated)
        {
            Voxels.Dispose();
        }
    }

    public void Dispose(JobHandle dependency)
    {
        if (IsCreated)
        {
            Voxels.Dispose(dependency);
        }
    }
}


// [新增] 托管组件，用于追踪异步GPU数据请求
public class GpuVoxelDataRequest : IComponentData
{
    public AsyncGPUReadbackRequest Request;
    // [修改] 这是一个临时的 NativeArray，用于接收GPU数据
    public NativeArray<Voxel> TempVoxelData;
    public ComputeBuffer Buffer;
}

// 存储26个邻居区块的实体引用
[InternalBufferCapacity(27)]
public struct ChunkNeighbor : IBufferElementData
{
    public Entity NeighborEntity;
}

// --- 托管组件，用于在系统之间传递网格引用 ---
public class GeneratedMesh : IComponentData
{
    public Mesh Mesh;
}


// --- 区块处理管道标签 ---
public struct RequestGpuDataTag : IComponentData, IEnableableComponent { }
public struct PendingGpuDataTag : IComponentData, IEnableableComponent { }
public struct RequestPaddingUpdateTag : IComponentData, IEnableableComponent { }
public struct RequestMeshTag : IComponentData, IEnableableComponent { }
public struct RequestColliderBakeTag : IComponentData, IEnableableComponent { }
public struct ChunkModifiedTag : IComponentData, IEnableableComponent { }
public struct IdleTag : IComponentData, IEnableableComponent { }