using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// [注意] 此处暂时保留原命名空间以减少修改，后续可统一
namespace Ruri.Voxel
{
    public abstract class ExecutorParameters
    {
        public string kernelName;
        public string commandBufferName;
        public bool updateInjected;
        // public ManagedTerrainCompiler compiler; // 简化，暂时移除
        public Ruri.Voxel.TerrainSeed seed;
    }

    public abstract class Executor<P> where P : ExecutorParameters
    {
        protected Dictionary<string, ExecutorTexture> textures;
        protected Dictionary<string, ExecutorBuffer> buffers;

        public Dictionary<string, ExecutorTexture> Textures => textures;
        public Dictionary<string, ExecutorBuffer> Buffers => buffers;

        protected virtual void CreateResources()
        {
            DisposeResources();
            textures = new Dictionary<string, ExecutorTexture>();
            buffers = new Dictionary<string, ExecutorBuffer>();
        }

        protected virtual void SetComputeParams(CommandBuffer commands, ComputeShader shader, P parameters, int kernelIndex)
        {
            commands.SetComputeIntParams(shader, "permutation_seed", new int[] { parameters.seed.permutationSeed.x, parameters.seed.permutationSeed.y, parameters.seed.permutationSeed.z });
            commands.SetComputeIntParams(shader, "modulo_seed", new int[] { parameters.seed.moduloSeed.x, parameters.seed.moduloSeed.y, parameters.seed.moduloSeed.z });
        }

        public GraphicsFence ExecuteWithInvocationCount(int3 invocations, ComputeShader shader, P parameters, GraphicsFence? previous = null)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (shader == null) throw new ArgumentNullException(nameof(shader));

            if (textures == null || buffers == null)
            {
                CreateResources();
            }

            int id = shader.FindKernel(parameters.kernelName);
            if(id == -1) throw new ArgumentException($"Kernel '{parameters.kernelName}' not found in shader '{shader.name}'.");

            CommandBuffer commands = new CommandBuffer
            {
                name = $"{parameters.commandBufferName} {parameters.kernelName}"
            };
            commands.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);

            if (previous.HasValue)
            {
                commands.WaitOnAsyncGraphicsFence(previous.Value, SynchronisationStageFlags.ComputeProcessing);
            }
            
            SetComputeParams(commands, shader, parameters, id);

            foreach (var (_, buffer) in buffers)
            {
                buffer.BindToComputeShader(commands, shader);
            }
            
            uint tx, ty, tz;
            shader.GetKernelThreadGroupSizes(id, out tx, out ty, out tz);
            int3 threadGroups = (int3)math.ceil((float3)invocations / new float3(tx, ty, tz));
            commands.DispatchCompute(shader, id, threadGroups.x, threadGroups.y, threadGroups.z);
            
            GraphicsFence fence = commands.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.ComputeProcessing);
            Graphics.ExecuteCommandBufferAsync(commands, ComputeQueueType.Background);
            commands.Dispose();
            return fence;
        }

        public virtual void DisposeResources()
        {
            if (buffers != null)
            {
                foreach (var (_, buffer) in buffers) buffer.Dispose();
                buffers = null;
            }
            // textures 暂不处理
        }

        ~Executor() => DisposeResources();
    }
    
    public abstract class VolumeExecutor<P> : Executor<P> where P : ExecutorParameters
    {
        protected int size;
        protected VolumeExecutor(int size)
        {
            if (size <= 0) throw new ArgumentException("Size must be a positive non-zero number");
            this.size = size;
        }

        public GraphicsFence Execute(ComputeShader shader, P parameters, GraphicsFence? previous = null)
        {
            return ExecuteWithInvocationCount(new int3(size), shader, parameters, previous);
        }

        protected override void SetComputeParams(CommandBuffer commands, ComputeShader shader, P parameters, int kernelIndex)
        {
            base.SetComputeParams(commands, shader, parameters, kernelIndex);
            commands.SetComputeIntParam(shader, "size", size);
        }
    }
    
    // 简化，暂时不需要Texture和Buffer的包装类
    public class ExecutorTexture {}
    public class ExecutorBuffer {
        public string name;
        public ComputeBuffer buffer;
        public ExecutorBuffer(string name, ComputeBuffer buffer)
        {
            this.name = name;
            this.buffer = buffer;
        }
        public virtual void BindToComputeShader(CommandBuffer commands, ComputeShader shader)
        {
            commands.SetGlobalBuffer(name + "_buffer", buffer);
        }
        public virtual void Dispose() => buffer?.Dispose();
    }
}