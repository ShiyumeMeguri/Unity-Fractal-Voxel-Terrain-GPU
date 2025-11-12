using OptIn.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class VoxelMeshBuilder
{
    public static void InitializeShaderParameter()
    {
        Shader.SetGlobalInt("_AtlasX", AtlasSize.x);
        Shader.SetGlobalInt("_AtlasY", AtlasSize.y);
        Shader.SetGlobalVector("_AtlasRec", new Vector4(1.0f / AtlasSize.x, 1.0f / AtlasSize.y));
    }

    public static readonly int2 AtlasSize = new int2(8, 8);

    public class NativeMeshData : System.IDisposable
    {
        public NativeArray<Voxel> nativeVoxels;
        public NativeArray<GPUVertex> nativeVertices;
        public NativeArray<int> nativeIndices;
        public NativeCounter counter;

        public NativeMeshData(int3 chunkSize)
        {
            int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
            int maxVertices = 12 * numVoxels;
            int maxIndices = 18 * numVoxels;

            nativeVoxels = new NativeArray<Voxel>(numVoxels, Allocator.Persistent);
            nativeVertices = new NativeArray<GPUVertex>(maxVertices, Allocator.Persistent);
            nativeIndices = new NativeArray<int>(maxIndices, Allocator.Persistent);
            counter = new NativeCounter(Allocator.Persistent);
        }

        public void GetMeshInformation(out int verticeSize, out int indicesSize)
        {
            verticeSize = counter.Count * 4;
            indicesSize = counter.Count * 6;
        }

        public void Dispose()
        {
            if (nativeVoxels.IsCreated) nativeVoxels.Dispose();
            if (nativeVertices.IsCreated) nativeVertices.Dispose();
            if (nativeIndices.IsCreated) nativeIndices.Dispose();
            if (counter.IsCreated) counter.Dispose();
        }
    }

    public static JobHandle ScheduleMeshingJob(NativeArray<Voxel> voxels, int3 chunkSize, NativeMeshData meshData, JobHandle dependency)
    {
        meshData.counter.Count = 0;

        var job = new VoxelMeshBuildJob
        {
            voxels = voxels,
            chunkSize = chunkSize,
            vertices = meshData.nativeVertices,
            indices = meshData.nativeIndices,
            counter = meshData.counter.ToConcurrent(),
        };
        return job.Schedule(dependency);
    }

    [BurstCompile]
    struct VoxelMeshBuildJob : IJob
    {
        [ReadOnly] public NativeArray<Voxel> voxels;
        [ReadOnly] public int3 chunkSize;
        [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<GPUVertex> vertices;
        [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<int> indices;
        public NativeCounter.Concurrent counter;

        private Voxel GetVoxelOrEmpty(int3 pos)
        {
            return VoxelUtil.BoundaryCheck(pos, chunkSize) ? voxels[VoxelUtil.To1DIndex(pos, chunkSize)] : Voxel.Empty;
        }

        private bool SignChanged(Voxel v1, Voxel v2) => v1.Density > 0 != v2.Density > 0;

        private float3 CalculateFeaturePoint(int3 pos)
        {
            float3 pointSum = float3.zero;
            int crossings = 0;
            for (int i = 0; i < 12; i++)
            {
                Voxel v1 = GetVoxelOrEmpty(pos + VoxelUtil.DC_VERT[VoxelUtil.DC_EDGE[i, 0]]);
                Voxel v2 = GetVoxelOrEmpty(pos + VoxelUtil.DC_VERT[VoxelUtil.DC_EDGE[i, 1]]);
                if (v1.IsIsosurface && v2.IsIsosurface && SignChanged(v1, v2))
                {
                    float t = math.unlerp(v1.Density, v2.Density, 0f);
                    if (!float.IsFinite(t)) t = 0.5f;
                    pointSum += math.lerp(pos + VoxelUtil.DC_VERT[VoxelUtil.DC_EDGE[i, 0]], pos + VoxelUtil.DC_VERT[VoxelUtil.DC_EDGE[i, 1]], t);
                    crossings++;
                }
            }
            return crossings > 0 ? pointSum / crossings : (float3)pos + 0.5f;
        }

        private float GetDensityForGradient(int3 pos, int3 chunkSize, [ReadOnly] NativeArray<Voxel> voxelData)
        {
            return voxelData[VoxelUtil.To1DIndex(pos, chunkSize)].Density;
        }

        private float3 CalculatePaddedGradient(int3 pos, int3 chunkSize, [ReadOnly] NativeArray<Voxel> voxelData)
        {
            float dx, dy, dz;

            // X-axis
            if (pos.x == 0)
                dx = GetDensityForGradient(pos + new int3(1, 0, 0), chunkSize, voxelData) - GetDensityForGradient(pos, chunkSize, voxelData);
            else if (pos.x == chunkSize.x - 1)
                dx = GetDensityForGradient(pos, chunkSize, voxelData) - GetDensityForGradient(pos - new int3(1, 0, 0), chunkSize, voxelData);
            else
                dx = (GetDensityForGradient(pos + new int3(1, 0, 0), chunkSize, voxelData) - GetDensityForGradient(pos - new int3(1, 0, 0), chunkSize, voxelData)) * 0.5f;

            // Y-axis
            if (pos.y == 0)
                dy = GetDensityForGradient(pos + new int3(0, 1, 0), chunkSize, voxelData) - GetDensityForGradient(pos, chunkSize, voxelData);
            else if (pos.y == chunkSize.y - 1)
                dy = GetDensityForGradient(pos, chunkSize, voxelData) - GetDensityForGradient(pos - new int3(0, 1, 0), chunkSize, voxelData);
            else
                dy = (GetDensityForGradient(pos + new int3(0, 1, 0), chunkSize, voxelData) - GetDensityForGradient(pos - new int3(0, 1, 0), chunkSize, voxelData)) * 0.5f;

            // Z-axis
            if (pos.z == 0)
                dz = GetDensityForGradient(pos + new int3(0, 0, 1), chunkSize, voxelData) - GetDensityForGradient(pos, chunkSize, voxelData);
            else if (pos.z == chunkSize.z - 1)
                dz = GetDensityForGradient(pos, chunkSize, voxelData) - GetDensityForGradient(pos - new int3(0, 0, 1), chunkSize, voxelData);
            else
                dz = (GetDensityForGradient(pos + new int3(0, 0, 1), chunkSize, voxelData) - GetDensityForGradient(pos - new int3(0, 0, 1), chunkSize, voxelData)) * 0.5f;

            float3 grad = new float3(dx, dy, dz);
            return math.normalizesafe(-grad, new float3(0, 1, 0));
        }

        public void Execute()
        {
            int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
            var gradients = new NativeArray<float3>(numVoxels, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            // Pass 1: Pre-calculate all gradients using a safe method for boundaries
            for (int x = 0; x < chunkSize.x; x++)
            {
                for (int y = 0; y < chunkSize.y; y++)
                {
                    for (int z = 0; z < chunkSize.z; z++)
                    {
                        var pos = new int3(x, y, z);
                        int index = VoxelUtil.To1DIndex(pos, chunkSize);
                        gradients[index] = CalculatePaddedGradient(pos, chunkSize, voxels);
                    }
                }
            }

            // Pass 2: Build mesh using pre-calculated gradients
            for (int x = 1; x < chunkSize.x - 1; x++)
                for (int y = 1; y < chunkSize.y - 1; y++)
                    for (int z = 1; z < chunkSize.z - 1; z++)
                    {
                        var pos = new int3(x, y, z);
                        var voxel = GetVoxelOrEmpty(pos);

                        // --- Block Meshing Logic ---
                        if (voxel.IsBlock)
                        {
                            for (int direction = 0; direction < 6; direction++)
                            {
                                Voxel neighborVoxel = GetVoxelOrEmpty(pos + VoxelUtil.VoxelDirectionOffsets[direction]);
                                // FIX: Generate a face if the neighbor is NOT a block.
                                // This treats smooth voxels (IsIsosurface) and Air as empty space,
                                // which is the desired behavior for creating a hard seam.
                                if (!neighborVoxel.IsBlock)
                                {
                                    AddQuadByDirection(direction, voxel.GetMaterialID(), 1.0f, 1.0f, pos - 1, counter.Increment(), vertices, indices);
                                }
                            }
                        }

                        // --- Smooth Meshing (Dual Contouring) Logic ---
                        for (int axis = 0; axis < 3; axis++)
                        {
                            var neighbor = GetVoxelOrEmpty(pos + VoxelUtil.DC_AXES[axis]);

                            // FIX: Generate a smooth face ONLY IF both the current voxel and its neighbor
                            // are of the isosurface type (which includes Air).
                            // This explicitly stops the smooth mesher from trying to blend with block voxels.
                            if (voxel.IsIsosurface && neighbor.IsIsosurface && SignChanged(voxel, neighbor))
                            {
                                int quadIndex = counter.Increment();
                                ushort materialId = voxel.Density > 0 ? voxel.GetMaterialID() : neighbor.GetMaterialID();

                                for (int i = 0; i < 4; i++)
                                {
                                    var cornerPos = pos + VoxelUtil.DC_ADJACENT[axis, i];
                                    vertices[quadIndex * 4 + i] = new GPUVertex
                                    {
                                        position = CalculateFeaturePoint(cornerPos) - 1,
                                        normal = gradients[VoxelUtil.To1DIndex(cornerPos, chunkSize)],
                                        uv = new float4(0, 0, materialId, 0)
                                    };
                                }

                                int vertIndex = quadIndex * 4;
                                if (voxel.Density > 0)
                                {
                                    indices[quadIndex * 6 + 0] = vertIndex + 0;
                                    indices[quadIndex * 6 + 1] = vertIndex + 1;
                                    indices[quadIndex * 6 + 2] = vertIndex + 2;
                                    indices[quadIndex * 6 + 3] = vertIndex + 0;
                                    indices[quadIndex * 6 + 4] = vertIndex + 2;
                                    indices[quadIndex * 6 + 5] = vertIndex + 3;
                                }
                                else
                                {
                                    indices[quadIndex * 6 + 0] = vertIndex + 0;
                                    indices[quadIndex * 6 + 1] = vertIndex + 2;
                                    indices[quadIndex * 6 + 2] = vertIndex + 1;
                                    indices[quadIndex * 6 + 3] = vertIndex + 0;
                                    indices[quadIndex * 6 + 4] = vertIndex + 3;
                                    indices[quadIndex * 6 + 5] = vertIndex + 2;
                                }
                            }
                        }
                    }

            gradients.Dispose();
        }
    }

    static void AddQuadByDirection(int direction, ushort materialID, float width, float height, int3 gridPosition, int quadIndex, NativeArray<GPUVertex> vertices, NativeArray<int> indices)
    {
        int vertexStart = quadIndex * 4;
        for (int i = 0; i < 4; i++)
        {
            float3 pos = VoxelUtil.CubeVertices[VoxelUtil.CubeFaces[i + direction * 4]];
            pos[VoxelUtil.DirectionAlignedX[direction]] *= width;
            pos[VoxelUtil.DirectionAlignedY[direction]] *= height;

            int atlasIndex = materialID * 6 + direction;
            int2 atlasPosition = new int2(atlasIndex % AtlasSize.x, atlasIndex / AtlasSize.x);

            vertices[vertexStart + i] = new GPUVertex
            {
                position = pos + gridPosition,
                normal = VoxelUtil.VoxelDirectionOffsets[direction],
                uv = new float4(VoxelUtil.CubeUVs[i].x * width, VoxelUtil.CubeUVs[i].y * height, atlasPosition.x, atlasPosition.y)
            };
        }

        int indexStart = quadIndex * 6;
        for (int i = 0; i < 6; i++)
        {
            indices[indexStart + i] = VoxelUtil.CubeIndices[i + direction * 6] + vertexStart;
        }
    }
}
