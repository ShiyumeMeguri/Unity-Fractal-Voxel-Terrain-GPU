using Unity.Entities;

// 标记一个区块的数据已被修改
public struct ChunkModifiedTag : IComponentData, IEnableableComponent { }

// 请求为该区块重新生成网格
public struct RequestMeshTag : IComponentData, IEnableableComponent { }

// 标记一个实体处于空闲状态
public struct IdleTag : IComponentData, IEnableableComponent { }