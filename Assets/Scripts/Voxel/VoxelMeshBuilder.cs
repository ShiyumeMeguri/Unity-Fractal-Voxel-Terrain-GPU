using System.Collections;
using OptIn.Voxel;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OptIn.Voxel
{
    public static class VoxelMeshBuilder
    {

        private static float[] CUBE_POS = new float[108]
        {
            0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f, 0f, 0f,
            0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f,
            0f, 1f, 1f, 0f, 1f, 1f, 1f, 1f, 0f, 0f,
            1f, 1f, 1f, 1f, 0f, 1f, 0f, 0f, 1f, 0f,
            0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 1f, 0f,
            0f, 1f, 0f, 1f, 0f, 1f, 1f, 1f, 1f, 1f,
            1f, 1f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f,
            1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 1f, 1f,
            0f, 0f, 0f, 0f, 1f, 1f, 0f, 1f, 0f, 0f,
            1f, 0f, 1f, 1f, 1f, 1f, 0f, 1f, 1f, 1f,
            0f, 1f, 0f, 1f, 1f, 0f, 0f, 1f
        };

        private static float[] CUBE_UV = new float[72]
        {
            1f, 0f, 1f, 1f, 0f, 1f, 1f, 0f, 0f, 1f,
            0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f, 1f, 0f,
            0f, 1f, 0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f,
            1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 1f, 1f,
            0f, 1f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f,
            1f, 1f, 0f, 1f, 1f, 0f, 0f, 1f, 0f, 0f,
            1f, 0f, 1f, 1f, 0f, 1f, 1f, 0f, 0f, 1f,
            0f, 0f
        };

        private static int[] CUBE_NORM = new int[108]
        {
            -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
            0, 0, -1, 0, 0, -1, 0, 0, 1, 0,
            0, 1, 0, 0, 1, 0, 0, 1, 0, 0,
            1, 0, 0, 1, 0, 0, 0, -1, 0, 0,
            -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
            0, 0, -1, 0, 0, 1, 0, 0, 1, 0,
            0, 1, 0, 0, 1, 0, 0, 1, 0, 0,
            1, 0, 0, 0, -1, 0, 0, -1, 0, 0,
            -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
            0, 0, 1, 0, 0, 1, 0, 0, 1, 0,
            0, 1, 0, 0, 1, 0, 0, 1
        };

        public static int3[] SN_VERT = new int3[8]
        {
            new int3(0, 0, 0),
            new int3(0, 0, 1),
            new int3(0, 1, 0),
            new int3(0, 1, 1),
            new int3(1, 0, 0),
            new int3(1, 0, 1),
            new int3(1, 1, 0),
            new int3(1, 1, 1)
        };

        private static int[,] EDGE = new int[12, 2]
        {
            { 0, 4 },
            { 1, 5 },
            { 2, 6 },
            { 3, 7 },
            { 5, 7 },
            { 1, 3 },
            { 4, 6 },
            { 0, 2 },
            { 4, 5 },
            { 0, 1 },
            { 6, 7 },
            { 2, 3 }
        };

        private static int3[] AXES = new int3[3]
        {
            new int3(1, 0, 0),
            new int3(0, 1, 0),
            new int3(0, 0, 1)
        };

        private static int3[,] ADJACENT = new int3[3, 6]
        {
            {
                new int3(0, 0, 0),
                new int3(0, -1, 0),
                new int3(0, -1, -1),
                new int3(0, -1, -1),
                new int3(0, 0, -1),
                new int3(0, 0, 0)
            },
            {
                new int3(0, 0, 0),
                new int3(0, 0, -1),
                new int3(-1, 0, -1),
                new int3(-1, 0, -1),
                new int3(-1, 0, 0),
                new int3(0, 0, 0)
            },
            {
                new int3(0, 0, 0),
                new int3(-1, 0, 0),
                new int3(-1, -1, 0),
                new int3(-1, -1, 0),
                new int3(0, -1, 0),
                new int3(0, 0, 0)
            }
        };

        /// <summary>
        ///   用于从任意栅格坐标读取 <see cref="Voxel"/> 的委托。
        ///   若坐标落在当前块外部，则自动查询相邻 Chunk，保证边界顶点一致。
        /// </summary>
        private delegate Voxel GetVoxelAt(int3 gridPos);
        public static void InitializeShaderParameter()
        {
            Shader.SetGlobalInt("_AtlasX", AtlasSize.x);
            Shader.SetGlobalInt("_AtlasY", AtlasSize.y);
            Shader.SetGlobalVector("_AtlasRec", new Vector4(1.0f / AtlasSize.x, 1.0f / AtlasSize.y));
        }

        public static readonly int2 AtlasSize = new int2(8, 8);

        public enum SimplifyingMethod
        {
            Culling,
            GreedyOnlyHeight,
            Greedy,
            GPUCulling,
        };

        public class NativeMeshData
        {
            NativeArray<Voxel> nativeVoxels;
            public NativeArray<GPUVertex> nativeVertices;
            public NativeArray<int> nativeIndices;
            public JobHandle jobHandle;

            public NativeMeshData(int3 chunkSize)
            {
                int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
                nativeVoxels = new NativeArray<Voxel>(numVoxels, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                nativeVertices = new NativeArray<GPUVertex>(0, Allocator.Persistent);
                nativeIndices = new NativeArray<int>(0, Allocator.Persistent);
            }

            ~NativeMeshData()
            {
                jobHandle.Complete();
                Dispose();
            }

            public void Dispose()
            {
                if (nativeVoxels.IsCreated) nativeVoxels.Dispose();
                if (nativeVertices.IsCreated) nativeVertices.Dispose();
                if (nativeIndices.IsCreated) nativeIndices.Dispose();
            }

            /// <summary>
            ///  根据本块及其邻居体素生成网格数据（Dual‑Contouring）。
            ///  ⚠️ 现在需要 <paramref name="ownerChunk"/> 参与，以解决边界裂缝问题。
            /// </summary>
            public IEnumerator ScheduleMeshingJob(
                Voxel[] selfVoxels,
                Chunk ownerChunk,
                int3 chunkSize,
                SimplifyingMethod method,
                bool argent = false)
            {
                /* 1. 把当前块体素复制进 NativeArray（Burst‑Job / GC‑free） */
                nativeVoxels.CopyFrom(selfVoxels);

                /* 2. 定义跨 Chunk 读取体素的委托 —— 优先从本块缓存取，越界时向生成器查询 */
                GetVoxelAt voxelAt = gridPos =>
                {
                    /* 在本块范围内 */
                    if (VoxelUtil.BoundaryCheck(gridPos, chunkSize))
                    {
                        int idx = VoxelUtil.To1DIndex(gridPos, chunkSize);
                        return nativeVoxels[idx];
                    }

                    /* 越界 → 折算成世界坐标，再委托 TerrainGenerator 查询邻居块 */
                    Vector3 worldPos = ownerChunk.transform.position +
                                       new Vector3(gridPos.x, gridPos.y, gridPos.z);

                    return TerrainGenerator.Instance.GetVoxel(worldPos, out var v) ? v : Voxel.Empty;
                };

                /* 3. 生成网格（核心算法与 B 版统一） */
                VertexBuffer vbuf = new VertexBuffer();
                DualContouringGenerateMesh(vbuf, voxelAt, chunkSize);

                /* 4. 转存到 NativeArray 供 Mesh API 使用 */
                int vCount = vbuf.Vertices.Count;
                int iCount = vbuf.Indices.Count == 0 ? vCount : vbuf.Indices.Count;

                if (nativeVertices.IsCreated) nativeVertices.Dispose();
                if (nativeIndices.IsCreated) nativeIndices.Dispose();

                nativeVertices = new NativeArray<GPUVertex>(vCount, Allocator.Persistent);
                nativeIndices = new NativeArray<int>(iCount, Allocator.Persistent);

                for (int i = 0; i < vCount; i++)
                {
                    var v = vbuf.Vertices[i];
                    nativeVertices[i] = new GPUVertex
                    {
                        position = v.pos,
                        normal = v.norm,
                        uv = new Vector4(v.uv.x, v.uv.y, 0f, 0f)
                    };
                }

                if (vbuf.Indices.Count == 0)
                {
                    for (int i = 0; i < iCount; i++)
                        nativeIndices[i] = i;
                }
                else
                {
                    for (int i = 0; i < iCount; i++)
                        nativeIndices[i] = vbuf.Indices[i];
                }

                /* 这里不再启动 JobHandle，直接同步拷贝即可 */
                yield return null;
            }

            public void GetMeshInformation(out int vertexSize, out int indexSize)
            {
                vertexSize = nativeVertices.Length;
                indexSize = nativeIndices.Length;
            }
        }

        /* ======================= Marching‑Cubes / Dual‑Contouring ======================= */

        private static void DualContouringGenerateMesh(VertexBuffer vbuf,
                                                      GetVoxelAt voxelAt,
                                                      int3 chunkSize)
        {
            for (int x = 0; x < chunkSize.x; x++)
                for (int y = 0; y < chunkSize.y; y++)
                    for (int z = 0; z < chunkSize.z; z++)
                    {
                        int3 pos = new int3(x, y, z);
                        Voxel center = voxelAt(pos);

                        for (int axis = 0; axis < 3; axis++)
                        {
                            Voxel neighbor = voxelAt(pos + AXES[axis]);
                            if (!SignChanged(center, neighbor))
                                continue;

                            for (int f = 0; f < 6; f++)
                            {
                                int faceIdx = (!IsDensityValid(center)) ? (5 - f) : f;
                                int3 cellPos = pos + ADJACENT[axis, faceIdx];

                                FeatureResult fr = ComputeFeaturePointAndNormal(voxelAt, cellPos);
                                float3 fp = fr.featurePoint;
                                if (!IsFinite(fp))
                                    fp = new float3(0f, -99f, 0f);

                                int texId = DetermineTexId(voxelAt, cellPos);
                                vbuf.PushVertex(cellPos + fp,
                                    new Vector2(texId, -1f),
                                    fr.normal);
                            }
                        }
                    }

            /* 若未写入索引则顺序输出 */
            if (vbuf.Indices.Count == 0)
                for (int i = 0; i < vbuf.Vertices.Count; i++) vbuf.Indices.Add(i);
        }


        // 辅助方法
        static Voxel GetVoxel(NativeArray<Voxel> voxels, int3 pos, int3 chunkSize)
        {
            pos.x = pos.x < 0 ? 0 : (pos.x >= chunkSize.x ? chunkSize.x - 1 : pos.x);
            pos.y = pos.y < 0 ? 0 : (pos.y >= chunkSize.y ? chunkSize.y - 1 : pos.y);
            pos.z = pos.z < 0 ? 0 : (pos.z >= chunkSize.z ? chunkSize.z - 1 : pos.z);
            int index = pos.z + pos.y * chunkSize.z + pos.x * (chunkSize.y * chunkSize.z);
            return voxels[index];
        }

        static bool SignChanged(Voxel a, Voxel b)
        {
            return (a.Density > 0f) != (b.Density > 0f);
        }

        static bool IsDensityValid(Voxel v)
        {
            return !v.IsDensityNil();
        }

        static bool IsFinite(float3 v)
        {
            return math.all(math.isfinite(v));
        }

        private struct FeatureResult
        {
            public float3 featurePoint;
            public float3 normal;
        }

        private static FeatureResult ComputeFeaturePointAndNormal(
            GetVoxelAt voxelAt, int3 pos)
        {
            /* 计算梯度 */
            float gx = voxelAt(pos + new int3(1, 0, 0)).Density - voxelAt(pos - new int3(1, 0, 0)).Density;
            float gy = voxelAt(pos + new int3(0, 1, 0)).Density - voxelAt(pos - new int3(0, 1, 0)).Density;
            float gz = voxelAt(pos + new int3(0, 0, 1)).Density - voxelAt(pos - new int3(0, 0, 1)).Density;
            float3 grad = new float3(gx, gy, gz) / 2f;
            float3 normal = math.normalize(-grad);

            /* 走遍 12 条边求交点平均 */
            float3 sum = float3.zero;
            int count = 0;

            for (int i = 0; i < 12; i++)
            {
                int3 v0 = SN_VERT[EDGE[i, 0]];
                int3 v1 = SN_VERT[EDGE[i, 1]];
                Voxel a = voxelAt(pos + v0);
                Voxel b = voxelAt(pos + v1);

                if (SignChanged(a, b))
                {
                    float t = math.unlerp(a.Density, b.Density, 0f);
                    float3 p = math.lerp(v0, v1, t);
                    sum += p;
                    count++;
                }
            }

            float3 feature = (count > 0) ? (sum / count) : float3.zero;
            return new FeatureResult { featurePoint = feature, normal = normal };
        }

        private static int DetermineTexId(GetVoxelAt voxelAt, int3 pos)
        {
            float minDensity = float.PositiveInfinity;
            int tex = 0;

            for (int i = 0; i < SN_VERT.Length; i++)
            {
                Voxel v = voxelAt(pos + SN_VERT[i]);
                if (!v.IsTexNil() && !v.IsDensityNil() && v.Density < minDensity)
                {
                    minDensity = v.Density;
                    tex = v.texId;
                }
            }
            return tex;
        }

    }
}
