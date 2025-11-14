using Ruri.Voxel;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ruri.Voxel
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MultiReadbackTransform
    {
        public Vector3 position;
        public float scale;
        public static readonly int size = sizeof(float) * 4;
    }

    public class MultiReadbackExecutorParameters : ExecutorParameters
    {
        public NativeArray<MultiReadbackTransform> transforms;
        public ComputeBuffer multiSignCountersBuffer;
    }

    public class MultiReadbackExecutor : VolumeExecutor<MultiReadbackExecutorParameters>
    {
        private ComputeBuffer transformsBuffer;
        private int[] defaultClearingInts;

        public MultiReadbackExecutor() : base(VoxelUtils.SIZE * VoxelUtils.MULTI_READBACK_CHUNK_SIZE_RATIO)
        {
            defaultClearingInts = new int[VoxelUtils.MULTI_READBACK_CHUNK_COUNT];
        }

        public override void DisposeResources()
        {
            base.DisposeResources();
            transformsBuffer?.Dispose();
            transformsBuffer = null;
        }

        protected override void CreateResources()
        {
            base.CreateResources();
            transformsBuffer = new ComputeBuffer(VoxelUtils.MULTI_READBACK_CHUNK_COUNT, MultiReadbackTransform.size, ComputeBufferType.Structured);

            // [核心修改] 使用你的 VoxelData 结构大小
            int voxelStructSize = UnsafeUtility.SizeOf<VoxelData>();
            buffers.Add("voxels", new ExecutorBuffer("voxels_buffer", new ComputeBuffer(VoxelUtils.VOLUME * VoxelUtils.MULTI_READBACK_CHUNK_COUNT, voxelStructSize, ComputeBufferType.Structured)));
        }

        protected override void SetComputeParams(CommandBuffer commands, ComputeShader shader, MultiReadbackExecutorParameters parameters, int kernelIndex)
        {
            base.SetComputeParams(commands, shader, parameters, kernelIndex);

            // [简化] 移除关键词设置，因为我们只有一个版本的Compute Shader
            
            transformsBuffer.SetData(parameters.transforms);
            commands.SetComputeBufferParam(shader, kernelIndex, "multi_transforms_buffer", transformsBuffer);

            parameters.multiSignCountersBuffer.SetData(defaultClearingInts);
            commands.SetComputeBufferParam(shader, kernelIndex, "multi_counters_buffer", parameters.multiSignCountersBuffer);

            // [核心修改] 将我们创建的体素数据缓冲区绑定到Shader
            commands.SetComputeBufferParam(shader, kernelIndex, "voxels_buffer", buffers["voxels"].buffer);
        }
    }
}