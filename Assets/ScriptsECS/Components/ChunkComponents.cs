// Components/ChunkComponents.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using System.Runtime.InteropServices;

namespace Ruri.Voxel
{
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelData
    {
        /// <summary>
        /// ID of the voxel.
        /// > 0: Block type ID.
        /// <= 0: Isosurface material ID (absolute value).
        /// = 0: Air.
        /// </summary>
        public short voxelID;

        /// <summary>
        /// For isosurface voxels (voxelID <= 0), this stores density, scaled to the range of a short.
        /// For block voxels (voxelID > 0), this can be used for metadata.
        /// </summary>
        public short metadata;

        public static VoxelData Empty => new VoxelData { voxelID = 0, metadata = 0 };

        public float Density
        {
            get
            {
                if (voxelID > 0) return 1f; // Blocks are always "full"
                return metadata / 32767f;
            }
            set
            {
                if (voxelID <= 0) // Only set density for isosurface
                {
                    metadata = (short)(math.clamp(value, -1f, 1f) * 32767f);
                }
            }
        }

        public bool IsBlock => voxelID > 0;
        public bool IsIsosurface => voxelID <= 0;
        public bool IsAir => voxelID == 0 && metadata <= 0;
        public bool IsSolid => IsBlock || Density > 0;
    }

    // 核心区块组件，存储位置和邻居状态
    public struct Chunk : IComponentData
    {
        public int3 Position;
        public byte SkirtMask;
        public FixedList64Bytes<Entity> Skirts; // 遵循框架，即使暂时不用
        public BitField32 NeighbourMask;
    }

    // 体素数据容器
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

    // 网格数据容器
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

    // --- 以下是完全遵循目标框架的状态标签 ---

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

    // 以下是为裙边预留的组件，即使暂时不实现其逻辑
    public struct TerrainSkirt : IComponentData
    {
        public byte Direction;
    }

    public struct TerrainSkirtLinkedParent : IComponentData
    {
        public Entity ChunkParent;
    }
}