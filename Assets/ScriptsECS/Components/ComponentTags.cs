using Unity.Entities;

namespace Ruri.Voxel
{
    /// <summary>
    /// 标记一个区块实体，表示它是一个地形区块。
    /// </summary>
    public struct TerrainChunkTag : IComponentData { }

    /// <summary>
    /// 请求从GPU回读体素数据。
    /// </summary>
    public struct TerrainChunkRequestReadbackTag : IComponentData, IEnableableComponent
    {
        public bool SkipMeshingIfEmpty;
    }

    /// <summary>
    /// 标记一个区块的体素数据已准备就绪，可以被后续系统（如网格生成）使用。
    /// </summary>
    public struct TerrainChunkVoxelsReadyTag : IComponentData, IEnableableComponent { }

    /// <summary>
    /// 请求为该区块生成或更新网格。
    /// </summary>
    public struct TerrainChunkRequestMeshingTag : IComponentData, IEnableableComponent
    {
        public bool DeferredVisibility;
    }

    /// <summary>
    /// 标记一个区块已完成所有生成流程，处于空闲状态。
    /// </summary>
    public struct TerrainChunkEndOfPipeTag : IComponentData, IEnableableComponent { }

    /// <summary>
    /// 用于延迟显示的标记，确保网格数据准备好后再渲染。
    /// </summary>
    public struct TerrainDeferredVisible : IComponentData, IEnableableComponent { }

}