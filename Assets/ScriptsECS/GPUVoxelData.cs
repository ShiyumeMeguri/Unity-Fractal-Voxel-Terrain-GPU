using System.Collections;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using OptIn.Voxel;
using UnityEngine.Rendering; // 添加 using

public class GPUVoxelData : System.IDisposable
{
    private int3 _CurrentChunkSize;
    private ComputeBuffer _VoxelBuffer;
    public ComputeBuffer VoxelBuffer => _VoxelBuffer;

    public GPUVoxelData(int3 initialChunkSize)
    {
        _CurrentChunkSize = initialChunkSize;
        AllocateBuffer(_CurrentChunkSize);
    }

    private void AllocateBuffer(int3 size)
    {
        int numVoxels = size.x * size.y * size.z;
        int voxelSize = UnsafeUtility.SizeOf<VoxelData>();
        _VoxelBuffer?.Release();
        _VoxelBuffer = new ComputeBuffer(numVoxels, voxelSize, ComputeBufferType.Default);
    }

    public IEnumerator Generate(VoxelData[] voxels, int3 chunkPosition, int3 newChunkSize, ComputeShader computeShader)
    {
        if (!newChunkSize.Equals(_CurrentChunkSize))
        {
            _CurrentChunkSize = newChunkSize;
            AllocateBuffer(_CurrentChunkSize);
        }

        int kernel = computeShader.FindKernel("CSMain");
        if (kernel < 0)
        {
            Debug.LogError("Could not find CSMain kernel!");
            yield break;
        }

        computeShader.SetBuffer(kernel, "asyncVoxelBuffer", _VoxelBuffer);
        computeShader.SetInts("chunkPosition", chunkPosition.x, chunkPosition.y, chunkPosition.z);
        computeShader.SetInts("chunkSize", newChunkSize.x, newChunkSize.y, newChunkSize.z);

        uint tx, ty, tz;
        computeShader.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);
        int3 threadGroupSize = new int3((int)tx, (int)ty, (int)tz);
        int3 groups = (newChunkSize + threadGroupSize - 1) / threadGroupSize;
        computeShader.Dispatch(kernel, groups.x, groups.y, groups.z);

        var request = AsyncGPUReadback.Request(_VoxelBuffer);
        yield return new WaitUntil(() => request.done);

        if (request.hasError)
        {
            Debug.LogError("GPU readback error for voxel data.");
            yield break;
        }

        var nativeData = request.GetData<VoxelData>();
        CopyNativeDataToManaged(voxels, nativeData, nativeData.Length);
    }

    private static unsafe void CopyNativeDataToManaged(VoxelData[] voxels, NativeArray<VoxelData> nativeData, int numVoxels)
    {
        if (voxels.Length < numVoxels) return;
        fixed (VoxelData* dest = voxels)
        {
            UnsafeUtility.MemCpy(dest, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(nativeData), numVoxels * (long)UnsafeUtility.SizeOf<VoxelData>());
        }
    }

    public void Dispose()
    {
        _VoxelBuffer?.Release();
        _VoxelBuffer = null;
    }
}