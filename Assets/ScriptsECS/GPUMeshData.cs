using System;
using System.Collections;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering; // 添加 using

[StructLayout(LayoutKind.Sequential)]
public struct GPUVertex
{
    public float3 position;
    public float3 normal;
    public float4 uv;
}

public class GPUMeshData : IDisposable
{
    public ComputeBuffer vertexBuffer;
    public ComputeBuffer indexBuffer;
    public ComputeBuffer counterBuffer;

    public int faceCount;

    private int m_MaxVertices;
    private int m_MaxIndices;

    public GPUMeshData(int3 chunkSize)
    {
        int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
        int maxFaces = numVoxels * 6;
        m_MaxVertices = maxFaces * 4;
        m_MaxIndices = maxFaces * 6;

        vertexBuffer = new ComputeBuffer(m_MaxVertices, Marshal.SizeOf(typeof(GPUVertex)), ComputeBufferType.Structured);
        indexBuffer = new ComputeBuffer(m_MaxIndices, sizeof(uint), ComputeBufferType.Structured);
        counterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Counter);
    }

    public IEnumerator Generate(ComputeShader meshComputeShader, ComputeBuffer voxelBuffer, int3 chunkPosition, int3 chunkSize, int2 atlasSize)
    {
        int clearKernel = meshComputeShader.FindKernel("ClearCounter");
        if (clearKernel < 0)
        {
            Debug.LogError("Could not find ClearCounter kernel!");
            yield break;
        }
        meshComputeShader.SetBuffer(clearKernel, "counterBuffer", counterBuffer);
        meshComputeShader.Dispatch(clearKernel, 1, 1, 1);

        int kernel = meshComputeShader.FindKernel("CSMain");
        if (kernel < 0)
        {
            Debug.LogError("Could not find CSMain kernel for GPU Meshing!");
            yield break;
        }

        meshComputeShader.SetBuffer(kernel, "voxelBuffer", voxelBuffer);
        meshComputeShader.SetBuffer(kernel, "vertexBuffer", vertexBuffer);
        meshComputeShader.SetBuffer(kernel, "indexBuffer", indexBuffer);
        meshComputeShader.SetBuffer(kernel, "counterBuffer", counterBuffer);
        meshComputeShader.SetInts("chunkPosition", chunkPosition.x, chunkPosition.y, chunkPosition.z);
        meshComputeShader.SetInts("chunkSize", chunkSize.x, chunkSize.y, chunkSize.z);
        meshComputeShader.SetInts("_AtlasSize", atlasSize.x, atlasSize.y);

        uint x, y, z;
        meshComputeShader.GetKernelThreadGroupSizes(kernel, out x, out y, out z);
        int3 groups = new int3(
            Mathf.CeilToInt((float)chunkSize.x / x),
            Mathf.CeilToInt((float)chunkSize.y / y),
            Mathf.CeilToInt((float)chunkSize.z / z)
        );
        meshComputeShader.Dispatch(kernel, groups.x, groups.y, groups.z);

        var request = AsyncGPUReadback.Request(counterBuffer);
        yield return new WaitUntil(() => request.done);

        if (request.hasError)
        {
            Debug.LogError("GPU readback error for mesh counter.");
            yield break;
        }

        faceCount = request.GetData<int>()[0];
    }

    public void Dispose()
    {
        vertexBuffer?.Release();
        indexBuffer?.Release();
        counterBuffer?.Release();
    }
}