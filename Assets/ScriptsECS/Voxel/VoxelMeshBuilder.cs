using OptIn.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class VoxelMeshBuilder
{
    public static readonly int2 AtlasSize = new int2(8, 8);

    public static JobHandle ScheduleMeshingJob(NativeArray<VoxelData> voxels, int3 chunkSize,
        NativeArray<GPUVertex> vertices, NativeArray<int> indices, NativeCounter counter,
        NativeArray<float3> precomputedNormals, JobHandle dependency)
    {
        counter.Count = 0;

        var job = new VoxelMeshBuildJob
        {
            voxels = voxels,
            chunkSize = chunkSize,
            vertices = vertices,
            indices = indices,
            counter = counter.ToConcurrent(),
            gradients = precomputedNormals
        };

        var handle = job.Schedule(dependency);
        JobHandle.ScheduleBatchedJobs();
        return handle;
    }

    [BurstCompile]
    private struct VoxelMeshBuildJob : IJob
    {
        [ReadOnly] public NativeArray<VoxelData> voxels;
        [ReadOnly] public int3 chunkSize;
        [ReadOnly] public NativeArray<float3> gradients;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<GPUVertex> vertices;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> indices;
        public NativeCounter.Concurrent counter;

        private VoxelData GetVoxelOrEmpty(int3 pos)
        {
            if (!VoxelUtils.BoundaryCheck(pos, chunkSize)) return VoxelData.Empty;
            return voxels[VoxelUtils.To1DIndex(pos, chunkSize)];
        }

        private bool SignChanged(VoxelData v1, VoxelData v2) => v1.Density > 0 != v2.Density > 0;

        private float3 CalculateFeaturePoint(int3 pos)
        {
            float3 pointSum = float3.zero;
            int crossings = 0;
            for (int i = 0; i < 12; i++)
            {
                var v1 = GetVoxelOrEmpty(pos + VoxelUtils.DC_VERT[VoxelUtils.DC_EDGE[i, 0]]);
                var v2 = GetVoxelOrEmpty(pos + VoxelUtils.DC_VERT[VoxelUtils.DC_EDGE[i, 1]]);
                if (v1.IsIsosurface && v2.IsIsosurface && SignChanged(v1, v2))
                {
                    float t = math.unlerp(v1.Density, v2.Density, 0f);
                    if (!float.IsFinite(t)) t = 0.5f;
                    pointSum += math.lerp((float3)(pos + VoxelUtils.DC_VERT[VoxelUtils.DC_EDGE[i, 0]]), (float3)(pos + VoxelUtils.DC_VERT[VoxelUtils.DC_EDGE[i, 1]]), t);
                    crossings++;
                }
            }
            return crossings > 0 ? pointSum / crossings : (float3)pos + 0.5f;
        }

        public void Execute()
        {
            for (int x = 1; x < chunkSize.x - 1; x++)
            {
                for (int y = 1; y < chunkSize.y - 1; y++)
                {
                    for (int z = 1; z < chunkSize.z - 1; z++)
                    {
                        var pos = new int3(x, y, z);
                        var voxel = GetVoxelOrEmpty(pos);

                        if (voxel.IsBlock)
                        {
                            for (int direction = 0; direction < 6; direction++)
                            {
                                if (!GetVoxelOrEmpty(pos + VoxelUtils.VoxelDirectionOffsets[direction]).IsBlock)
                                {
                                    AddQuadByDirection(direction, voxel.GetMaterialID(), pos - 1, counter.Increment(), vertices, indices);
                                }
                            }
                        }

                        for (int axis = 0; axis < 3; axis++)
                        {
                            var neighbor = GetVoxelOrEmpty(pos + VoxelUtils.DC_AXES[axis]);
                            if (voxel.IsIsosurface && neighbor.IsIsosurface && SignChanged(voxel, neighbor))
                            {
                                int quadIndex = counter.Increment();
                                ushort materialId = voxel.Density > 0 ? voxel.GetMaterialID() : neighbor.GetMaterialID();

                                for (int i = 0; i < 4; i++)
                                {
                                    var cornerPos = pos + VoxelUtils.DC_ADJACENT[axis, i];
                                    vertices[quadIndex * 4 + i] = new GPUVertex
                                    {
                                        position = CalculateFeaturePoint(cornerPos) - 1,
                                        normal = gradients[VoxelUtils.To1DIndex(cornerPos, chunkSize)],
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
                }
            }
        }
    }

    private static void AddQuadByDirection(int direction, ushort materialID, int3 gridPosition, int quadIndex, NativeArray<GPUVertex> vertices, NativeArray<int> indices)
    {
        int vertexStart = quadIndex * 4;
        for (int i = 0; i < 4; i++)
        {
            float3 pos = VoxelUtils.CubeVertices[VoxelUtils.CubeFaces[i + direction * 4]];
            int atlasIndex = materialID * 6 + direction;
            int2 atlasPosition = new int2(atlasIndex % AtlasSize.x, atlasIndex / AtlasSize.x);

            vertices[vertexStart + i] = new GPUVertex
            {
                position = pos + gridPosition,
                normal = VoxelUtils.VoxelDirectionOffsets[direction],
                uv = new float4(VoxelUtils.CubeUVs[i].x, VoxelUtils.CubeUVs[i].y, atlasPosition.x, atlasPosition.y)
            };
        }

        int indexStart = quadIndex * 6;
        int[] cubeIndices_tris = { 0, 1, 2, 0, 2, 3 }; // Correct winding for standard quad
        for (int i = 0; i < 6; i++)
        {
            indices[indexStart + i] = vertexStart + cubeIndices_tris[i];
        }
    }
}