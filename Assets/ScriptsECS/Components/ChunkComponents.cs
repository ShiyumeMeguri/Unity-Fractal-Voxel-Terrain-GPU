// Components/ChunkComponents.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using UnityEngine; // For Bounds

namespace Ruri.Voxel
{
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelData
    {
        public short voxelID;
        public short metadata;

        public static VoxelData Empty => new VoxelData { voxelID = 0, metadata = 0 };

        public float Density
        {
            get
            {
                if (voxelID > 0) return 1f;
                return metadata / 32767f;
            }
            set
            {
                metadata = (short)(math.clamp(value, -1f, 1f) * 32767f);
            }
        }

        public bool IsBlock => voxelID > 0;
        public bool IsIsosurface => voxelID <= 0;
        public bool IsAir => voxelID == 0 && metadata <= 0;
        public bool IsSolid => IsBlock || Density > 0;

        public ushort GetMaterialID()
        {
            return (ushort)math.abs(voxelID);
        }
    }

    public struct Chunk : IComponentData
    {
        public int3 Position;
        // [移除] 移除 SkirtMask 和 Skirts 字段，因为简化版引擎不需要
        public BitField32 NeighbourMask;
    }

    public struct TerrainChunkVoxels : IComponentData, IEnableableComponent
    {
        public NativeArray<VoxelData> Voxels;
        public JobHandle AsyncWriteJobHandle;
        public JobHandle AsyncReadJobHandle;
        public bool MeshingInProgress;

        public bool IsCreated => Voxels.IsCreated;

        public void Dispose()
        {
            if (IsCreated)
            {
                JobHandle.CombineDependencies(AsyncReadJobHandle, AsyncWriteJobHandle).Complete();
                Voxels.Dispose();
            }
        }
    }

    public struct TerrainChunkMesh : IComponentData, IEnableableComponent
    {
        public NativeArray<float3> Vertices;
        public NativeArray<float3> Normals;
        public NativeArray<float4> UVs;
        public NativeArray<int> MainMeshIndices;
        public JobHandle AccessJobHandle;

        public static TerrainChunkMesh FromJobHandlerStats(MeshJobHandler.Stats stats)
        {
            var vertices = new NativeArray<float3>(stats.VertexCount, Allocator.Persistent);
            var normals = new NativeArray<float3>(stats.VertexCount, Allocator.Persistent);
            var uvs = new NativeArray<float4>(stats.VertexCount, Allocator.Persistent);
            var indices = new NativeArray<int>(stats.MainMeshIndexCount, Allocator.Persistent);

            if (stats.VertexCount > 0)
            {
                stats.Vertices.positions.GetSubArray(0, stats.VertexCount).CopyTo(vertices);
                stats.Vertices.normals.GetSubArray(0, stats.VertexCount).CopyTo(normals);
                stats.Vertices.uvs.GetSubArray(0, stats.VertexCount).CopyTo(uvs);
            }
            if (stats.MainMeshIndexCount > 0)
            {
                stats.MainMeshIndices.GetSubArray(0, stats.MainMeshIndexCount).CopyTo(indices);
            }

            return new TerrainChunkMesh
            {
                Vertices = vertices,
                Normals = normals,
                UVs = uvs,
                MainMeshIndices = indices,
            };
        }

        public void Dispose()
        {
            AccessJobHandle.Complete();
            if (Vertices.IsCreated) Vertices.Dispose();
            if (Normals.IsCreated) Normals.Dispose();
            if (UVs.IsCreated) UVs.Dispose();
            if (MainMeshIndices.IsCreated) MainMeshIndices.Dispose();
        }
    }

    // --- 状态标签 (State Machine Tags) ---
    public struct TerrainChunkRequestReadbackTag : IComponentData, IEnableableComponent
    {
        public bool SkipMeshingIfEmpty;
    }
    public struct TerrainChunkVoxelsReadyTag : IComponentData, IEnableableComponent { }
    public struct TerrainChunkRequestMeshingTag : IComponentData, IEnableableComponent
    {
        public bool DeferredVisibility;
    }
    public struct TerrainChunkRequestCollisionTag : IComponentData, IEnableableComponent { }
    public struct TerrainChunkEndOfPipeTag : IComponentData, IEnableableComponent { }
    public struct TerrainDeferredVisible : IComponentData, IEnableableComponent { }
}