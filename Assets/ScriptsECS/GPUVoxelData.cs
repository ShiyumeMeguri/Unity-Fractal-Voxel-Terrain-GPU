using System.Collections;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using OptIn.Voxel;
using UnityEngine.Rendering; // 添加 using

public class GPUVoxelData : System.IDisposable
{
    private int3 m_CurrentChunkSize;
    private ComputeBuffer m_VoxelBuffer;
    public ComputeBuffer VoxelBuffer => m_VoxelBuffer;

    public GPUVoxelData(int3 initialChunkSize)
    {
        m_CurrentChunkSize = initialChunkSize;
        AllocateBuffer(m_CurrentChunkSize);
    }

    private void AllocateBuffer(int3 size)
    {
        int numVoxels = size.x * size.y * size.z;
        int voxelSize = UnsafeUtility.SizeOf<Voxel>();
        m_VoxelBuffer?.Release();
        m_VoxelBuffer = new ComputeBuffer(numVoxels, voxelSize, ComputeBufferType.Default);
    }

    public IEnumerator Generate(Voxel[] voxels, int3 chunkPosition, int3 newChunkSize, ComputeShader computeShader)
    {
        if (!newChunkSize.Equals(m_CurrentChunkSize))
        {
            m_CurrentChunkSize = newChunkSize;
            AllocateBuffer(m_CurrentChunkSize);
        }

        int kernel = computeShader.FindKernel("CSMain");
        if (kernel < 0)
        {
            Debug.LogError("Could not find CSMain kernel!");
            yield break;
        }

        computeShader.SetBuffer(kernel, "asyncVoxelBuffer", m_VoxelBuffer);
        computeShader.SetInts("chunkPosition", chunkPosition.x, chunkPosition.y, chunkPosition.z);
        computeShader.SetInts("chunkSize", newChunkSize.x, newChunkSize.y, newChunkSize.z);

        uint tx, ty, tz;
        computeShader.GetKernelThreadGroupSizes(kernel, out tx, out ty, out tz);
        int3 threadGroupSize = new int3((int)tx, (int)ty, (int)tz);
        int3 groups = (newChunkSize + threadGroupSize - 1) / threadGroupSize;
        computeShader.Dispatch(kernel, groups.x, groups.y, groups.z);

        var request = AsyncGPUReadback.Request(m_VoxelBuffer);
        yield return new WaitUntil(() => request.done);

        if (request.hasError)
        {
            Debug.LogError("GPU readback error for voxel data.");
            yield break;
        }

        var nativeData = request.GetData<Voxel>();
        CopyNativeDataToManaged(voxels, nativeData, nativeData.Length);
    }

    private static unsafe void CopyNativeDataToManaged(Voxel[] voxels, NativeArray<Voxel> nativeData, int numVoxels)
    {
        if (voxels.Length < numVoxels) return;
        fixed (Voxel* dest = voxels)
        {
            UnsafeUtility.MemCpy(dest, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(nativeData), numVoxels * (long)UnsafeUtility.SizeOf<Voxel>());
        }
    }

    public void Dispose()
    {
        m_VoxelBuffer?.Release();
        m_VoxelBuffer = null;
    }
}