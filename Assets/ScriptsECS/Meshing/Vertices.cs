// Meshing/Vertices.cs
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ruri.Voxel
{
    public struct Vertices
    {
        public struct Single
        {
            public float3 position;
            public float3 normal;
            public float4 uv;      // [新增] 用于存储UV和材质信息
            public float4 colour;

            public void Add(float3 startVertex, float3 endVertex, int startIndex, int endIndex, ref NativeArray<VoxelData> voxels, ref NativeArray<float3> voxelNormals)
            {
                VoxelData start = voxels[startIndex];
                VoxelData end = voxels[endIndex];
                float value = math.unlerp(start.Density, end.Density, 0);
                AddLerped(startVertex, endVertex, startIndex, endIndex, value, ref voxels, ref voxelNormals);
            }

            public void AddLerped(float3 startVertex, float3 endVertex, int startIndex, int endIndex, float value, ref NativeArray<VoxelData> voxels, ref NativeArray<float3> voxelNormals)
            {
                position += math.lerp(startVertex, endVertex, value);
                normal += math.lerp(voxelNormals[startIndex], voxelNormals[endIndex], value);
                // UVs 和其他数据将在 Meshing Job 中直接填充，这里不作插值
            }

            public void Finalize(int count)
            {
                if (count > 0)
                {
                    position /= count;
                    normal = math.normalizesafe(normal, new float3(0, 1, 0));
                }
            }
        }

        public NativeArray<float3> positions;
        public NativeArray<float3> normals;
        public NativeArray<float4> uvs;
        public NativeArray<float4> colours;

        public Vertices(int count, Allocator allocator)
        {
            positions = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
            normals = new NativeArray<float3>(count, allocator, NativeArrayOptions.UninitializedMemory);
            uvs = new NativeArray<float4>(count, allocator, NativeArrayOptions.UninitializedMemory);
            colours = new NativeArray<float4>(count, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public Single this[int index]
        {
            get => new Single { position = positions[index], normal = normals[index], uv = uvs[index], colour = colours[index] };
            set
            {
                positions[index] = value.position;
                normals[index] = value.normal;
                uvs[index] = value.uv;
                colours[index] = value.colour;
            }
        }

        public void SetMeshDataAttributes(int count, Mesh.MeshData data)
        {
            if (count == 0) return;

            // [修正] 顶点属性描述符现在有4个流
            var descriptors = new NativeArray<VertexAttributeDescriptor>(4, Allocator.Temp)
            {
                [0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4),
                [1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                [2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                [3] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 4)
            };
            data.SetVertexBufferParams(count, descriptors);
            descriptors.Dispose();

            // Stream 0: Position
            var dstPositions = data.GetVertexData<half4>();
            for (int i = 0; i < count; i++)
            {
                dstPositions[i] = (half4)new float4(positions[i], 0);
            }

            // Stream 1: Normal
            var dstNormals = data.GetVertexData<uint>(stream: 1); // [修正] 明确指定流索引
            for (int i = 0; i < count; i++)
            {
                dstNormals[i] = BitUtils.PackSnorm8(new float4(normals[i], 0));
            }

            // Stream 2: Color
            var dstColours = data.GetVertexData<uint>(stream: 2);
            for (int i = 0; i < count; i++)
            {
                dstColours[i] = BitUtils.PackUnorm8(new float4(1, 1, 1, 1)); // 默认为白色
            }

            // Stream 3: TexCoord0 (UV)
            var dstUVs = data.GetVertexData<half4>(stream: 3); // [修正] 获取TexCoord0的数据流
            for (int i = 0; i < count; i++)
            {
                dstUVs[i] = (half4)uvs[i]; // 将我们的uvs数据写入
            }
        }

        public void Dispose()
        {
            if (positions.IsCreated) positions.Dispose();
            if (normals.IsCreated) normals.Dispose();
            if (uvs.IsCreated) uvs.Dispose(); // [新增]
            if (colours.IsCreated) colours.Dispose();
        }
    }
}