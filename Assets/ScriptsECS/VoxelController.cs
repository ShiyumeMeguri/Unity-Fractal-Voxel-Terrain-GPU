using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class VoxelController : MonoBehaviour
{
    [Header("Block Editing")]
    [Tooltip("The ID for blocks placed with the right mouse button.")]
    [SerializeField] private short blockMaterialId = 1; // e.g., Stone

    private EntityManager _EntityManager;

    private void Start()
    {
        // 获取默认世界中的EntityManager
        _EntityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    void Update()
    {
        HandleBlockEditing();
    }

    private void HandleBlockEditing()
    {
        // 放置方块
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                float3 placePosition = hit.point - (float3)ray.direction * 0.01f;
                CreateEditRequest(placePosition, blockMaterialId);
            }
        }

        // 移除方块
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                float3 removePosition = hit.point + (float3)ray.direction * 0.01f;
                CreateEditRequest(removePosition, 0); // VoxelID 0 是空气
            }
        }
    }

    private void CreateEditRequest(float3 worldPosition, short voxelID)
    {
        // 创建一个实体来承载编辑请求
        Entity requestEntity = _EntityManager.CreateEntity();
        
        // 添加VoxelEditRequest组件并设置其数据
        _EntityManager.AddComponentData(requestEntity, new VoxelEditRequest
        {
            Type = VoxelEditRequest.EditType.SetBlock,
            WorldPosition = worldPosition,
            VoxelID = voxelID
        });
    }
}