using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ruri.Voxel
{
    /// <summary>
    /// CPU端的体素数据结构 (AoS)。
    /// 严格遵守 4 字节布局: short + short。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelData
    {
        public short voxelID;
        public short metadata;
    }

    /// <summary>
    /// GPU端的体素数据结构，与CPU端布局完全一致，以便直接内存拷贝。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuVoxel
    {
        public short voxelID;
        public short metadata;
    }

    /// <summary>
    /// 一个Burst Job，用于将GPU回读的数据（GpuVoxel）拷贝到CPU端的NativeArray<VoxelData>。
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public unsafe struct GpuToCpuCopy : IJobParallelFor
    {
        public NativeArray<VoxelData> cpuData;

        [NativeDisableUnsafePtrRestriction]
        public GpuVoxel* rawGpuData;
        
        public void Execute(int index)
        {
            // 直接进行指针到结构的内存拷贝
            cpuData[index] = *(rawGpuData + index);
        }
    }
}