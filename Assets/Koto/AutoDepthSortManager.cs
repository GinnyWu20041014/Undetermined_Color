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
                    targetObject.GetComponentInChildren<SpriteRenderer>(true) == null)
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
            float cameraDepth = targetCamera.transform
                .InverseTransformPoint(sortingGroup.transform.position).z;
            int sortingOrder = Mathf.RoundToInt(-cameraDepth * sortingScale);

            sortingGroup.sortingLayerName = sortingLayerName;
            sortingGroup.sortingOrder = sortingOrder;
        }
    }
}
