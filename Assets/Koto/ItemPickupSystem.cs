using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家在掃描模式中撿取 item Tag 物件，並將其圖片依序顯示於三個 Canvas 格位。
/// </summary>
public class ItemPickupSystem : MonoBehaviour
{
    [Header("撿取設定")]
    [InspectorName("掃描系統")]
    [Tooltip("只有此掃描系統處於掃描模式時才可以撿取物品；未指定時會自動尋找同物件上的掃描系統。")]
    [SerializeField] private ScanningSystem scanningSystem = null;

    [InspectorName("撿取按鍵")]
    [Tooltip("在掃描模式中按下此按鍵，撿取範圍內最近的 item 物件。")]
    [SerializeField] private KeyCode pickupKey = KeyCode.E;

    [InspectorName("攻擊腳本")]
    [Tooltip("撿取範圍會自動使用此攻擊腳本的攻擊範圍半徑。")]
    [SerializeField] private PlayerAttack playerAttack = null;

    [InspectorName("物品 Tag")]
    [Tooltip("可被撿取的地圖物件必須使用此 Tag；預設為 item。")]
    [SerializeField] private string itemTag = "item";

    [Header("Canvas 三個物品格")]
    [InspectorName("第一格 Image")]
    [Tooltip("第一個撿取物品要顯示的 UI Image。")]
    [SerializeField] private Image firstSlotImage = null;

    [InspectorName("第二格 Image")]
    [Tooltip("第二個撿取物品要顯示的 UI Image。")]
    [SerializeField] private Image secondSlotImage = null;

    [InspectorName("第三格 Image")]
    [Tooltip("第三個撿取物品要顯示的 UI Image。")]
    [SerializeField] private Image thirdSlotImage = null;

    private int nextSlotIndex;

    private void Awake()
    {
        if (scanningSystem == null)
        {
            scanningSystem = GetComponent<ScanningSystem>();
        }

        if (playerAttack == null)
        {
            playerAttack = GetComponent<PlayerAttack>();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(pickupKey))
        {
            TryPickupNearestItem();
        }
    }

    /// <summary>可由 UI 按鈕或其他腳本呼叫的撿取入口。</summary>
    public void TryPickupNearestItem()
    {
        if (scanningSystem == null || !scanningSystem.IsScanning)
        {
            Debug.Log("【撿取系統】目前不是掃描模式，無法撿取物品。", this);
            return;
        }

        Image availableSlot = GetNextAvailableSlot();
        if (availableSlot == null)
        {
            Debug.Log("【撿取系統】三個物品格已滿，無法再撿取。", this);
            return;
        }

        GameObject nearestItem = FindNearestItem();
        if (nearestItem == null)
        {
            Debug.Log("【撿取系統】撿取範圍內沒有 item 物件。", this);
            return;
        }

        Sprite itemSprite = GetItemSprite(nearestItem);
        if (itemSprite == null)
        {
            Debug.LogWarning("【撿取系統】item 物件找不到可顯示的 Sprite，無法撿取。", nearestItem);
            return;
        }

        availableSlot.sprite = itemSprite;
        availableSlot.preserveAspect = true;
        availableSlot.gameObject.SetActive(true);
        nextSlotIndex++;

        nearestItem.SetActive(false);
        Debug.Log($"【撿取系統】已撿取：{nearestItem.name}，已放入第 {nextSlotIndex} 格。", this);
    }

    private GameObject FindNearestItem()
    {
        if (string.IsNullOrWhiteSpace(itemTag))
        {
            return null;
        }

        GameObject[] items;
        try
        {
            items = GameObject.FindGameObjectsWithTag(itemTag);
        }
        catch (UnityException)
        {
            Debug.LogWarning($"【撿取系統】找不到 Tag：{itemTag}。請先在 Unity 的 Tags 新增它。", this);
            return null;
        }

        GameObject nearestItem = null;
        float pickupRange = GetPickupRange();
        float nearestDistanceSquared = pickupRange * pickupRange;
        foreach (GameObject item in items)
        {
            Vector3 offset = item.transform.position - transform.position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestItem = item;
            }
        }

        return nearestItem;
    }

    private Image GetNextAvailableSlot()
    {
        Image[] slots = { firstSlotImage, secondSlotImage, thirdSlotImage };
        while (nextSlotIndex < slots.Length && slots[nextSlotIndex] == null)
        {
            nextSlotIndex++;
        }

        return nextSlotIndex < slots.Length ? slots[nextSlotIndex] : null;
    }

    private float GetPickupRange()
    {
        return playerAttack != null ? playerAttack.AttackRange : 0f;
    }

    private static Sprite GetItemSprite(GameObject item)
    {
        SpriteRenderer spriteRenderer = item.GetComponentInChildren<SpriteRenderer>(true);
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return spriteRenderer.sprite;
        }

        Image image = item.GetComponentInChildren<Image>(true);
        return image != null ? image.sprite : null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, GetPickupRange());
    }
}
