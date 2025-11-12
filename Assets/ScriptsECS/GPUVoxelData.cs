using System.Collections;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using OptIn.Voxel;
using Tuntenfisch.Generics;
using Tuntenfisch.Extensions;

public class GPUVoxelData : System.IDisposable
{
    private int3 currentChunkSize;
    private AsyncComputeBuffer _asyncVoxelBuffer;
    public AsyncComputeBuffer asyncVoxelBuffer => _asyncVoxelBuffer;

    public GPUVoxelData(int3 initialChunkSize)
    {
        currentChunkSize = initialChunkSize;
        AllocateBuffer(currentChunkSize);
    }

    private void AllocateBuffer(int3 size)
    {
        int numVoxels = size.x * size.y * size.z;
        int voxelSize = UnsafeUtility.SizeOf<Voxel>();
        _asyncVoxelBuffer?.Release();
        _asyncVoxelBuffer = new AsyncComputeBuffer(numVoxels, voxelSize, ComputeBufferType.Default);
    }

    public IEnumerator Generate(Voxel[] voxels, int3 chunkPosition, int3 newChunkSize, ComputeShader computeShader)
    {
        if (!newChunkSize.Equals(currentChunkSize))
        {
            currentChunkSize = newChunkSize;
            AllocateBuffer(currentChunkSize);
        }

        int numVoxels = newChunkSize.x * newChunkSize.y * newChunkSize.z;
        int kernel = computeShader.FindKernel("CSMain");
        if (kernel < 0)
        {
            Debug.LogError("未能找到 ComputeShader 内核 CSMain！");
            yield break;
        }

        computeShader.SetBuffer(kernel, "asyncVoxelBuffer", _asyncVoxelBuffer);
        computeShader.SetInts("chunkPosition", chunkPosition.x, chunkPosition.y, chunkPosition.z);
        computeShader.SetInts("chunkSize", newChunkSize.x, newChunkSize.y, newChunkSize.z);
        int3 threadGroupSize = new int3(8);  // 匹配 HLSL numthreads
        int3 groups = (newChunkSize + threadGroupSize - 1) / threadGroupSize;  // ceil(34/4)=9
        computeShader.Dispatch(kernel, groups.x, groups.y, groups.z);

        var nativeData = new NativeArray<Voxel>(numVoxels, Allocator.Persistent);
        _asyncVoxelBuffer.StartReadbackNonAlloc(ref nativeData, numVoxels);

        while (!_asyncVoxelBuffer.IsDataAvailable())
            yield return null;
        _asyncVoxelBuffer.EndReadback();

        CopyNativeDataToManaged(voxels, nativeData, numVoxels);
        nativeData.Dispose();
    }

    private static unsafe void CopyNativeDataToManaged(Voxel[] voxels, NativeArray<Voxel> nativeData, int numVoxels)
    {
        fixed (Voxel* dest = voxels)
        {
            UnsafeUtility.MemCpy(dest, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(nativeData), numVoxels * (long)UnsafeUtility.SizeOf<Voxel>());
        }
    }

    public void Dispose()
    {
        _asyncVoxelBuffer?.Release();
        _asyncVoxelBuffer = null;
    }
}