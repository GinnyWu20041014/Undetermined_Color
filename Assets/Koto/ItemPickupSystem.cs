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

    [Header("放置設定")]
    [InspectorName("放置按鍵")]
    [Tooltip("按下此按鍵，將最先撿取的物品放置到範圍內最近的放置目標。")]
    [SerializeField] private KeyCode placementKey = KeyCode.Q;

    [InspectorName("放置目標 Tag")]
    [Tooltip("可放置物品的目標物件必須使用此 Tag。")]
    [SerializeField] private string placementTargetTag = "placementTarget";

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

    private readonly GameObject[] pickedItems = new GameObject[3];

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

        if (Input.GetKeyDown(placementKey))
        {
            TryPlaceFirstItem();
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

        int availableSlotIndex = GetNextAvailableSlotIndex();
        if (availableSlotIndex < 0)
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

        Image availableSlot = GetSlots()[availableSlotIndex];
        availableSlot.sprite = itemSprite;
        availableSlot.preserveAspect = true;
        availableSlot.gameObject.SetActive(true);
        pickedItems[availableSlotIndex] = nearestItem;

        nearestItem.SetActive(false);
        Debug.Log($"【撿取系統】已撿取：{nearestItem.name}，已放入第 {availableSlotIndex + 1} 格。", this);
    }

    /// <summary>將最先撿取、尚未放置的物品放到範圍內最近的指定 Tag 目標。</summary>
    public void TryPlaceFirstItem()
    {
        int pickedSlotIndex = GetFirstPickedSlotIndex();
        if (pickedSlotIndex < 0)
        {
            Debug.Log("【撿取系統】目前沒有可放置的物品。", this);
            return;
        }

        GameObject placementTarget = FindNearestTaggedObject(placementTargetTag);
        if (placementTarget == null)
        {
            Debug.Log($"【撿取系統】攻擊範圍內找不到 Tag 為「{placementTargetTag}」的放置目標。", this);
            return;
        }

        GameObject item = pickedItems[pickedSlotIndex];
        item.transform.position = placementTarget.transform.position;
        item.SetActive(true);

        Image itemSlot = GetSlots()[pickedSlotIndex];
        itemSlot.sprite = null;
        pickedItems[pickedSlotIndex] = null;
        Debug.Log($"【撿取系統】已將 {item.name} 放置到：{placementTarget.name}。", this);
    }

    private GameObject FindNearestItem()
    {
        return FindNearestTaggedObject(itemTag);
    }

    private GameObject FindNearestTaggedObject(string targetTag)
    {
        if (string.IsNullOrWhiteSpace(targetTag))
        {
            return null;
        }

        GameObject[] taggedObjects;
        try
        {
            taggedObjects = GameObject.FindGameObjectsWithTag(targetTag);
        }
        catch (UnityException)
        {
            Debug.LogWarning($"【撿取系統】找不到 Tag：{targetTag}。請先在 Unity 的 Tags 新增它。", this);
            return null;
        }

        GameObject nearestObject = null;
        float interactionRange = GetInteractionRange();
        float nearestDistanceSquared = interactionRange * interactionRange;
        foreach (GameObject taggedObject in taggedObjects)
        {
            Vector3 offset = taggedObject.transform.position - transform.position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestObject = taggedObject;
            }
        }

        return nearestObject;
    }

    private Image[] GetSlots()
    {
        return new[] { firstSlotImage, secondSlotImage, thirdSlotImage };
    }

    private int GetNextAvailableSlotIndex()
    {
        Image[] slots = GetSlots();
        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (slots[slotIndex] != null && pickedItems[slotIndex] == null)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private int GetFirstPickedSlotIndex()
    {
        for (int slotIndex = 0; slotIndex < pickedItems.Length; slotIndex++)
        {
            if (pickedItems[slotIndex] != null)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private float GetInteractionRange()
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
        Gizmos.DrawWireSphere(transform.position, GetInteractionRange());
    }
}
