// Components/ChunkComponents.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace Ruri.Voxel
{
    // [修正] 你的Voxel结构体被重命名为VoxelData以匹配框架，并移到此处
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

    // [修正] 遵循框架API，定义核心区块组件
    public struct Chunk : IComponentData
    {
        public int3 Position;
        public byte SkirtMask;
        public FixedList64Bytes<Entity> Skirts;
        public BitField32 NeighbourMask;
    }

    // [修正] 遵循框架API，定义体素数据组件
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

    // [修正] 遵循框架API，定义网格数据组件
    public struct TerrainChunkMesh : IComponentData, IEnableableComponent
    {
        public NativeArray<float3> Vertices;
        public NativeArray<float3> Normals;
        public NativeArray<int> MainMeshIndices;
        public JobHandle AccessJobHandle;

        public static TerrainChunkMesh FromJobHandlerStats(MeshJobHandler.Stats stats)
        {
            var vertices = new NativeArray<float3>(stats.VertexCount, Allocator.Persistent);
            var normals = new NativeArray<float3>(stats.VertexCount, Allocator.Persistent);
            var indices = new NativeArray<int>(stats.MainMeshIndexCount, Allocator.Persistent);

            if (stats.VertexCount > 0)
            {
                stats.Vertices.positions.GetSubArray(0, stats.VertexCount).CopyTo(vertices);
                stats.Vertices.normals.GetSubArray(0, stats.VertexCount).CopyTo(normals);
            }
            if (stats.MainMeshIndexCount > 0)
            {
                stats.MainMeshIndices.GetSubArray(0, stats.MainMeshIndexCount).CopyTo(indices);
            }

            return new TerrainChunkMesh
            {
                Vertices = vertices,
                Normals = normals,
                MainMeshIndices = indices,
            };
        }

        public void Dispose()
        {
            AccessJobHandle.Complete();
            if (Vertices.IsCreated) Vertices.Dispose();
            if (Normals.IsCreated) Normals.Dispose();
            if (MainMeshIndices.IsCreated) MainMeshIndices.Dispose();
        }
    }

    // --- [新增] 以下是完全遵循目标框架的状态标签 ---
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
    public struct TerrainSkirt : IComponentData
    {
        public byte Direction;
    }
    public struct TerrainSkirtLinkedParent : IComponentData
    {
        public Entity ChunkParent;
    }
}