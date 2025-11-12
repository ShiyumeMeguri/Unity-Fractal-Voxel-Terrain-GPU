using System;
using System.Collections;
using System.Runtime.InteropServices;
using Tuntenfisch.Extensions;
using Tuntenfisch.Generics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
public struct GPUVertex
{
    public float3 position;
    public float3 normal;
    public float4 uv;
}
public class GPUMeshData : IDisposable
{
    // GPU 计算生成的数据缓冲区
    public AsyncComputeBuffer vertexBuffer;   // RWStructuredBuffer<GPUVertex>
    public AsyncComputeBuffer indexBuffer;    // RWStructuredBuffer<uint>
    public AsyncComputeBuffer counterBuffer;  // 用于计数生成的面数（Counter 类型）

    public int faceCount;

    int maxVertices;
    int maxIndices;

    public GPUMeshData(int3 chunkSize)
    {
        int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
        int maxFaces = numVoxels * 6;
        maxVertices = maxFaces * 4;
        maxIndices = maxFaces * 6;

        vertexBuffer = new AsyncComputeBuffer(maxVertices, Marshal.SizeOf(typeof(GPUVertex)), ComputeBufferType.Structured);
        indexBuffer = new AsyncComputeBuffer(maxIndices, sizeof(uint), ComputeBufferType.Structured);
        // 创建单元素计数器缓冲区
        counterBuffer = new AsyncComputeBuffer(1, sizeof(uint), ComputeBufferType.Counter);
    }

    /// <summary>
    /// 根据传入的 ComputeShader 生成网格数据，逻辑与 CPU 版完全一致。
    /// 要求 ComputeShader 包含内核 ClearCounter 与 CSMain。
    /// </summary>
    public IEnumerator Generate(ComputeShader meshComputeShader, ComputeBuffer voxelBuffer, int3 chunkPosition, int3 chunkSize, int2 atlasSize)
    {
        // ① 清零 counterBuffer：使用 ClearCounter 内核
        int clearKernel = meshComputeShader.FindKernel("ClearCounter");
        if (clearKernel < 0)
        {
            Debug.LogError("未能找到清零计数器的内核 ClearCounter！");
            yield break;
        }
        meshComputeShader.SetBuffer(clearKernel, "counterBuffer", counterBuffer);
        meshComputeShader.Dispatch(clearKernel, 1, 1, 1);

        // ② 设置 CSMain 内核参数
        int kernel = meshComputeShader.FindKernel("CSMain");
        if (kernel < 0)
        {
            Debug.LogError("未能找到 GPU Mesh 生成的 ComputeShader 内核 CSMain！");
            yield break;
        }
        meshComputeShader.SetBuffer(kernel, "voxelBuffer", voxelBuffer);
        meshComputeShader.SetBuffer(kernel, "vertexBuffer", vertexBuffer);
        meshComputeShader.SetBuffer(kernel, "indexBuffer", indexBuffer);
        meshComputeShader.SetBuffer(kernel, "counterBuffer", counterBuffer);
        meshComputeShader.SetInts("chunkPosition", chunkPosition.x, chunkPosition.y, chunkPosition.z);
        meshComputeShader.SetInts("chunkSize", chunkSize.x, chunkSize.y, chunkSize.z);
        meshComputeShader.SetInts("_AtlasSize", atlasSize.x, atlasSize.y);

        // 拓展函数 自动根据线程计算group数量 
        meshComputeShader.Dispatch(kernel, chunkSize);

        // ⑤ 按照 AsyncComputeBuffer 回读流程获取生成的面数
        NativeArray<int> counterData = new NativeArray<int>(1, Allocator.Persistent);
        counterBuffer.StartReadbackNonAlloc(ref counterData, 1);
        while (!counterBuffer.IsDataAvailable())
            yield return null;
        counterBuffer.EndReadback();

        faceCount = counterData[0];
        counterData.Dispose();

        yield break;
    }

    public void Dispose()
    {
        if (vertexBuffer != null) vertexBuffer.Release();
        if (indexBuffer != null) indexBuffer.Release();
        if (counterBuffer != null) counterBuffer.Release();
    }
}