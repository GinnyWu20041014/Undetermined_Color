using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 依物件在鏡頭前方的深度，自動處理指定 Tag 的 Sprite 前後遮擋。
/// 鏡頭較近的物件會有較高的 Sorting Order，並顯示在較遠物件前方。
/// </summary>
public class AutoDepthSortManager : MonoBehaviour
{
    [Header("要排序的 Tag")]
    [Tooltip("可增加多個 Tag；只有這些 Tag 的物件會參與前後遮擋排序。")]
    [SerializeField] private List<string> targetTags = new List<string>();

    [Header("鏡頭與圖層")]
    [Tooltip("用於判斷前後深度的鏡頭；未指定時會自動使用 Main Camera。")]
    [SerializeField] private Camera targetCamera = null;
    [Tooltip("所有受管理的 Sprite 會使用此 Sorting Layer，避免不同圖層覆蓋距離排序。")]
    [SerializeField] private string sortingLayerName = "Default";

    [Header("排序設定")]
    [Tooltip("深度轉換為 Sorting Order 的精細度。數值越大，前後差距越清楚。")]
    [SerializeField, Min(1f)] private float sortingScale = 1000f;
    [Tooltip("重新搜尋新增或刪除物件的間隔。")]
    [SerializeField, Min(0.01f)] private float refreshInterval = 0.5f;

    private readonly List<SortingGroup> sortingGroups = new List<SortingGroup>();
    private float refreshTimer;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void OnEnable()
    {
        RefreshTargets();
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null)
            {
                return;
            }
        }

        refreshTimer += Time.deltaTime;
        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0f;
            RefreshTargets();
        }

        UpdateSortingOrders();
    }

    private void RefreshTargets()
    {
        sortingGroups.Clear();
        HashSet<GameObject> foundObjects = new HashSet<GameObject>();

        foreach (string tag in targetTags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            GameObject[] taggedObjects;
            try
            {
                taggedObjects = GameObject.FindGameObjectsWithTag(tag);
            }
            catch (UnityException)
            {
                Debug.LogWarning($"【前後遮擋排序】找不到 Tag：{tag}。", this);
                continue;
            }

            foreach (GameObject targetObject in taggedObjects)
            {
                if (!foundObjects.Add(targetObject) ||
                    targetObject.GetComponentInChildren<Renderer>(true) == null)
                {
                    continue;
                }

                SortingGroup sortingGroup = targetObject.GetComponent<SortingGroup>();
                if (sortingGroup == null)
                {
                    sortingGroup = targetObject.AddComponent<SortingGroup>();
                }

                sortingGroups.Add(sortingGroup);
            }
        }
    }

    private void UpdateSortingOrders()
    {
        foreach (SortingGroup sortingGroup in sortingGroups)
        {
            if (sortingGroup == null)
            {
                continue;
            }

            // cameraSpace.z 是物件沿鏡頭正前方的真正前後深度；
            // 不是受左右位置影響的直線距離。
            int sortingOrder = GetSortingOrder(GetSortingPoint(sortingGroup));

            sortingGroup.sortingLayerName = sortingLayerName;
            sortingGroup.sortingOrder = sortingOrder;
        }
    }

    // Particle effects share the sprites' camera, layer and depth scale.
    public bool ApplyDepthSorting(Renderer targetRenderer, Vector3 worldPosition, int orderOffset)
    {
        if (targetRenderer == null || !isActiveAndEnabled) return false;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return false;
        targetRenderer.sortingLayerName = sortingLayerName;
        targetRenderer.sortingOrder = Mathf.Clamp(GetSortingOrder(worldPosition) + Mathf.Clamp(orderOffset, -32768, 32767), -32768, 32767);
        return true;
    }

    private int GetSortingOrder(Vector3 worldPosition)
    {
        float cameraDepth = targetCamera.transform.InverseTransformPoint(worldPosition).z;
        return Mathf.Clamp(Mathf.RoundToInt(-cameraDepth * sortingScale), -32768, 32767);
    }

    private Vector3 GetSortingPoint(SortingGroup sortingGroup)
    {
        if (sortingGroup.gameObject.name != "B_8_3")
        {
            return sortingGroup.transform.position;
        }

        GameObject[] players;
        try
        {
            players = GameObject.FindGameObjectsWithTag("Player");
        }
        catch (UnityException)
        {
            return sortingGroup.transform.position;
        }

        if (players.Length == 0)
        {
            return sortingGroup.transform.position;
        }

        // Find the closest point on every renderer's local bounds to the player.
        // This follows the object's actual rotated bounds instead of its pivot.
        float closestDistance = float.PositiveInfinity;
        Vector3 sortingPoint = sortingGroup.transform.position;
        Renderer[] renderers = sortingGroup.GetComponentsInChildren<Renderer>(true);

        foreach (GameObject player in players)
        {
            Vector3 playerPosition = player.transform.position;
            foreach (Renderer targetRenderer in renderers)
            {
                Transform rendererTransform = targetRenderer.transform;
                Vector3 localPlayerPosition = rendererTransform.InverseTransformPoint(playerPosition);
                Vector3 localClosestPoint = targetRenderer.localBounds.ClosestPoint(localPlayerPosition);
                Vector3 worldClosestPoint = rendererTransform.TransformPoint(localClosestPoint);
                float distance = (worldClosestPoint - playerPosition).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    sortingPoint = worldClosestPoint;
                }
            }
        }

        return sortingPoint;
    }

    public bool ApplyDepthSorting(SortingGroup targetGroup, Vector3 worldPosition, int orderOffset)
    {
        if (targetGroup == null || !isActiveAndEnabled) return false;
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return false;
        targetGroup.sortingLayerName = sortingLayerName;
        targetGroup.sortingOrder = Mathf.Clamp(GetSortingOrder(worldPosition) +
            Mathf.Clamp(orderOffset, -32768, 32767), -32768, 32767);
        return true;
    }
}
