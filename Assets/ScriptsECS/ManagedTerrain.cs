// Behaviours/ManagedTerrain.cs
using UnityEngine;

// 严格遵循目标框架的单例模式，用于在ECS系统中访问托管资源（如ComputeShader）
public class ManagedTerrain : MonoBehaviour
{
    public static ManagedTerrain Instance;

    // 在Authoring中烘焙的资源将被Systems引用
    [HideInInspector]
    public TerrainResources Resources;

    void Awake()
    {
        Instance = this;
    }
    
    // 供System在OnCreate时调用，以获取烘焙好的资源
    public void SetResources(TerrainResources resources)
    {
        this.Resources = resources;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        Instance = null;
    }
}