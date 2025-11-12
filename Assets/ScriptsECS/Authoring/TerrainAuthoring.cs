using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class TerrainAuthoring : MonoBehaviour
{
    public int3 ChunkSize = new int3(32, 32, 32);
    public int3 PaddedChunkSize => ChunkSize + 2;
    public int2 ChunkSpawnSize = new int2(8, 8);
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
    public int MeshJobsPerTick = 4;

    class Baker : Baker<TerrainAuthoring>
    {
        public override void Bake(TerrainAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new TerrainConfig
            {
                ChunkSize = authoring.ChunkSize,
                PaddedChunkSize = authoring.PaddedChunkSize,
                ChunkSpawnSize = authoring.ChunkSpawnSize,
            });

            AddComponent(entity, new TerrainMesherConfig
            {
                MeshJobsPerTick = authoring.MeshJobsPerTick
            });

            AddComponentObject(entity, new TerrainResources
            {
                ChunkMaterial = authoring.ChunkMaterial,
                VoxelComputeShader = authoring.VoxelComputeShader
            });
        }
    }
}

public class TerrainResources : IComponentData
{
    public Material ChunkMaterial;
    public ComputeShader VoxelComputeShader;
}

public struct TerrainMesherConfig : IComponentData
{
    public int MeshJobsPerTick;
}