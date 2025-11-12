// Assets/ScriptsECS/Meshing/MeshJobHandler.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace OptIn.Voxel.Meshing
{
    public class MeshJobHandler
    {
        public struct Stats
        {
            public bool IsEmpty;
            public Bounds Bounds;
            public int VertexCount;
            public int MainMeshIndexCount;
            public int[] ForcedSkirtFacesTriCount;
            public Vertices Vertices;
            public NativeArray<int> MainMeshIndices;
        }

        private CoreMeshHandler m_Core;
        private SkirtHandler m_Skirt;
        private MergeMeshHandler m_Merger;
        private ApplyMeshHandler m_Apply;
        private NormalsHandler m_Normals;

        private JobHandle m_JobHandle;
        private Entity m_Entity;

        public MeshJobHandler()
        {
            m_Core = new CoreMeshHandler();
            m_Skirt = new SkirtHandler();
            m_Merger = new MergeMeshHandler();
            m_Apply = new ApplyMeshHandler();
            m_Normals = new NormalsHandler();
        }

        public void Init(TerrainConfig config)
        {
            m_Core.Init(config);
            m_Skirt.Init(config);
            m_Merger.Init(config);
            m_Apply.Init(config);
            m_Normals.Init(config);
        }

        public bool IsFree { get; private set; } = true;

        public bool IsComplete(EntityManager manager) => m_JobHandle.IsCompleted && !IsFree && manager.Exists(m_Entity);

        public void BeginJob(Entity entity, ref TerrainChunkVoxels chunkVoxels, EntityManager mgr, JobHandle dependency)
        {
            IsFree = false;
            m_Entity = entity;

            var deps = new NativeArray<JobHandle>(4, Allocator.Temp);
            deps[0] = dependency;
            deps[1] = m_JobHandle;
            deps[2] = chunkVoxels.AsyncReadJobHandle;
            deps[3] = chunkVoxels.AsyncWriteJobHandle;
            JobHandle currentHandle = JobHandle.CombineDependencies(deps);
            deps.Dispose();

            m_Normals.Schedule(ref chunkVoxels, currentHandle);
            currentHandle = m_Normals.JobHandle;

            m_Core.Schedule(ref chunkVoxels, ref m_Normals, currentHandle);

            m_Skirt.Schedule(ref chunkVoxels, ref m_Core, ref m_Normals, m_Core.JobHandle);

            m_Merger.Schedule(ref m_Core, ref m_Skirt);

            m_Apply.Schedule(ref m_Merger);

            m_JobHandle = m_Apply.JobHandle;
            chunkVoxels.AsyncReadJobHandle = m_JobHandle;
        }

        public bool TryComplete(EntityManager mgr, out Mesh outMesh, out Entity outEntity, out Stats outStats)
        {
            m_JobHandle.Complete();
            IsFree = true;

            outMesh = null;
            outEntity = Entity.Null;
            outStats = default;

            if (!mgr.Exists(m_Entity)) return false;

            outEntity = m_Entity;
            bool isEmpty = m_Merger.TotalVertexCount.Value == 0;

            outStats = new Stats
            {
                Bounds = new Bounds { min = m_Apply.Bounds.Value.Min, max = m_Apply.Bounds.Value.Max },
                VertexCount = m_Merger.TotalVertexCount.Value,
                MainMeshIndexCount = m_Merger.SubmeshIndexCounts[0],
                ForcedSkirtFacesTriCount = m_Skirt.ForcedTriangleCounter.ToArray(),
                IsEmpty = isEmpty,
                Vertices = m_Merger.MergedVertices,
                MainMeshIndices = m_Merger.MergedIndices,
            };

            if (isEmpty)
            {
                // [修复] Mesh.MeshDataArray 没有 IsCreated 属性，直接 Dispose
                m_Apply.MeshDataArray.Dispose();
            }
            else
            {
                outMesh = new Mesh { name = "VoxelChunkMesh", indexFormat = IndexFormat.UInt32 };
                Mesh.ApplyAndDisposeWritableMeshData(m_Apply.MeshDataArray, outMesh, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            }

            return true;
        }

        public void Dispose()
        {
            m_JobHandle.Complete();
            m_Core.Dispose();
            m_Skirt.Dispose();
            m_Merger.Dispose();
            m_Apply.Dispose();
            m_Normals.Dispose();
        }
    }
}