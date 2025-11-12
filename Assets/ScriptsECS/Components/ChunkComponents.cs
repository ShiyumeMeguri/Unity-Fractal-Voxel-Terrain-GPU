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

// 存储区块的体素数据
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
public class GpuVoxelDataRequest : IComponentData, IDisposable
{
    public AsyncGPUReadbackRequest Request;
    public NativeArray<Voxel> TempVoxelData; // 临时数据存储
    public ComputeBuffer Buffer;             // 使用的ComputeBuffer

    public void Dispose()
    {
        if (TempVoxelData.IsCreated) TempVoxelData.Dispose();
        Buffer?.Release(); // 安全释放
    }
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

// 托管组件，用于在系统之间传递网格引用以进行碰撞体烘焙
public class GeneratedMesh : IComponentData
{
    public Mesh Mesh;
}

// --- 区块处理管道标签 ---
// 刚创建的区块
public struct NewChunkTag : IComponentData, IEnableableComponent { }
// 请求GPU生成体素数据
public struct RequestGpuDataTag : IComponentData, IEnableableComponent { }
// 正在等待GPU数据回读
public struct PendingGpuDataTag : IComponentData, IEnableableComponent { }
// 请求更新边界Padding体素
public struct RequestPaddingUpdateTag : IComponentData, IEnableableComponent { }
// 请求生成网格
public struct RequestMeshTag : IComponentData, IEnableableComponent { }
// 正在等待网格生成作业完成
public struct PendingMeshTag : IComponentData, IEnableableComponent { }
// 请求烘焙碰撞体
public struct RequestColliderBakeTag : IComponentData, IEnableableComponent { }
// 区块被修改，需要重新网格化
public struct ChunkModifiedTag : IComponentData, IEnableableComponent { }
// 处理完成，处于空闲状态
public struct IdleTag : IComponentData, IEnableableComponent { }