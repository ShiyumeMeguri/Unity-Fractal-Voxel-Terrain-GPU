// Assets/ScriptsECS/Meshing/Vertices.cs

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptIn.Voxel.Meshing
{
    public struct Vertices
    {
        public struct Single
        {
            public float3 position;
            public float3 normal;
            public float4 layers;
            public float4 colour;
        }

        public NativeArray<float3> positions;
        public NativeArray<float3> normals;
        public NativeArray<float4> layers;
        public NativeArray<float4> colours;

        public Vertices(int count, Allocator allocator)
        {
            positions = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
            normals = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
            layers = new NativeArray<float4>(count, allocator, NativeArrayOptions.UninitializedMemory);
            colours = new NativeArray<float4>(count, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public void CopyTo(Vertices dst, int dstOffset, int length)
        {
            if (length > 0)
            {
                NativeArray<float3>.Copy(positions, 0, dst.positions, dstOffset, length);
                NativeArray<float3>.Copy(normals, 0, dst.normals, dstOffset, length);
                NativeArray<float4>.Copy(layers, 0, dst.layers, dstOffset, length);
                NativeArray<float4>.Copy(colours, 0, dst.colours, dstOffset, length);
            }
        }

        public Vertices GetSubArray(int offset, int length)
        {
            return new Vertices
            {
                positions = positions.GetSubArray(offset, length),
                normals = normals.GetSubArray(offset, length),
                layers = layers.GetSubArray(offset, length),
                colours = colours.GetSubArray(offset, length),
            };
        }

        public void SetMeshDataAttributes(int count, Mesh.MeshData data)
        {
            var descriptors = new NativeArray<VertexAttributeDescriptor>(4, Allocator.Temp)
            {
                [0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                [1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                [2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
                [3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4)
            };
            data.SetVertexBufferParams(count, descriptors);
            descriptors.Dispose();

            if (count > 0)
            {
                positions.GetSubArray(0, count).CopyTo(data.GetVertexData<float3>(0));
                normals.GetSubArray(0, count).CopyTo(data.GetVertexData<float3>(1));
                colours.GetSubArray(0, count).CopyTo(data.GetVertexData<float4>(2));
                // Note: 'layers' are not standard vertex attributes, often packed into TexCoord or Color.
                // Here we'll pack them into TexCoord0 for demonstration.
                layers.GetSubArray(0, count).CopyTo(data.GetVertexData<float4>(3));
            }
        }

        public void Dispose()
        {
            if (positions.IsCreated) positions.Dispose();
            if (normals.IsCreated) normals.Dispose();
            if (layers.IsCreated) layers.Dispose();
            if (colours.IsCreated) colours.Dispose();
        }
    }
}