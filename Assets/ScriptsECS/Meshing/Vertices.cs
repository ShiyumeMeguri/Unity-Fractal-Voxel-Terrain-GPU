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

            public void Add(float3 startVertex, float3 endVertex, int startIndex, int endIndex, ref NativeArray<VoxelData> voxels, ref NativeArray<float3> voxelNormals)
            {
                var start = voxels[startIndex];
                var end = voxels[endIndex];
                float value = math.unlerp(start.Density, end.Density, 0);
                AddLerped(startVertex, endVertex, startIndex, endIndex, value, ref voxels, ref voxelNormals);
            }

            public void AddLerped(float3 startVertex, float3 endVertex, int startIndex, int endIndex, float value, ref NativeArray<VoxelData> voxels, ref NativeArray<float3> voxelNormals)
            {
                position += math.lerp(startVertex, endVertex, value);
                normal += math.lerp(voxelNormals[startIndex], voxelNormals[endIndex], value);
                // layers and colours can be interpolated here if needed
            }

            public void Finalize(int count)
            {
                if (count > 0)
                {
                    position /= count;
                    normal = math.normalizesafe(normal);
                    layers /= count;
                }
            }
        }

        public NativeArray<float3> positions;
        public NativeArray<float3> normals;
        public NativeArray<float4> layers;
        public NativeArray<float4> colours;

        public Vertices(int count, Allocator allocator)
        {
            positions = new NativeArray<float3>(count, allocator);
            normals = new NativeArray<float3>(count, allocator);
            layers = new NativeArray<float4>(count, allocator);
            colours = new NativeArray<float4>(count, allocator);
        }

        public Single this[int index]
        {
            get => new Single { position = positions[index], normal = normals[index], layers = layers[index], colour = colours[index] };
            set { positions[index] = value.position; normals[index] = value.normal; layers[index] = value.layers; colours[index] = value.colour; }
        }

        public void SetMeshDataAttributes(int count, Mesh.MeshData data)
        {
            if (count == 0) return;

            var descriptors = new NativeArray<VertexAttributeDescriptor>(4, Allocator.Temp)
            {
                [0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                [1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 1),
                [2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, stream: 2),
                [3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, stream: 3)
            };
            data.SetVertexBufferParams(count, descriptors);
            descriptors.Dispose();

            positions.GetSubArray(0, count).CopyTo(data.GetVertexData<float3>(stream: 0));
            normals.GetSubArray(0, count).CopyTo(data.GetVertexData<float3>(stream: 1));
            colours.GetSubArray(0, count).CopyTo(data.GetVertexData<float4>(stream: 2));
            layers.GetSubArray(0, count).CopyTo(data.GetVertexData<float4>(stream: 3));
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