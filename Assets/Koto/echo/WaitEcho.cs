using UnityEngine;

/// <summary>
/// 由撿取／放置系統通知物品已放下或取回的介面。
/// </summary>
public interface IItemPlacementListener
{
    void OnItemPlaced(GameObject placementTarget);
    void OnItemPickedUp();
}

/// <summary>
/// 等待回響：放下時自動選取一個機關，取回前只會暫停該機關。
/// </summary>
public class WaitEcho : MonoBehaviour, IItemPlacementListener
{
    [Header("機關搜尋")]
    [InspectorName("機關作用範圍")]
    [Tooltip("初始回響在此 X/Z 平面半徑內，只固定最近的 placementTarget 機關。")]
    [Min(0.01f)]
    [SerializeField] private float mechanismSearchRange = 2f;

    private MechanismStateController pausedMechanism = null;
    private PuzzleDoor heldDoor = null;
    private bool isWaiting;
    private bool initialPlacementChecked;

    private void Start() => InitializeNearbyMechanism();
    private void OnDestroy() => RetrieveEcho();

    public void InitializeNearbyMechanism()
    {
        if (initialPlacementChecked) return;
        initialPlacementChecked = true;
        EchoPickupState state = GetComponentInParent<EchoPickupState>(true);
        if (isWaiting || (state != null && (state.HasBeenPickedUp || state.IsInInventory || state.IsConsumed))) return;
        GameObject target = EchoInitialPlacement.FindNearest(transform.position, mechanismSearchRange,
            candidate => EchoInitialPlacement.FindDoor(candidate) != null || EchoInitialPlacement.FindMechanism(candidate) != null);
        if (target != null) StartWaitingAt(transform.position, target);
    }

    /// <summary>由物品放置系統呼叫；會選取放置位置對應的一個機關。</summary>
    public void OnItemPlaced(GameObject placementTarget)
    {
        initialPlacementChecked = true;
        Vector3 placementPosition = placementTarget != null
            ? placementTarget.transform.position
            : transform.position;
        StartWaitingAt(placementPosition, placementTarget);
    }

    /// <summary>由物品撿取系統呼叫；恢復本回響先前選取的機關。</summary>
    public void OnItemPickedUp()
    {
        initialPlacementChecked = true;
        RetrieveEcho();
    }

    /// <summary>可由其他腳本手動呼叫，在回響目前位置選取一個機關。</summary>
    public void StartWaiting()
    {
        StartWaitingAt(transform.position, null);
    }

    /// <summary>取回回響，恢復它目前暫停的機關。</summary>
    public void RetrieveEcho()
    {
        if (!isWaiting)
        {
            return;
        }

        if (pausedMechanism != null)
        {
            pausedMechanism.ResumeMechanism(this);
        }
        if (heldDoor != null)
        {
            heldDoor.ReleaseDoor(this);
        }

        pausedMechanism = null;
        heldDoor = null;
        isWaiting = false;
        Debug.Log("【等待回響】回響已取回，機關已恢復原本狀態。", this);
    }

    private void StartWaitingAt(Vector3 position, GameObject placementTarget)
    {
        if (isWaiting)
        {
            return;
        }

        if (placementTarget == null)
            placementTarget = EchoInitialPlacement.FindNearest(position, mechanismSearchRange,
                candidate => EchoInitialPlacement.FindDoor(candidate) != null || EchoInitialPlacement.FindMechanism(candidate) != null);
        if (placementTarget == null) return;

        StreetLampController lamp = StreetLampController.FindOnPlacementTarget(placementTarget);
        if (lamp != null)
        {
            pausedMechanism = lamp.Mechanism;
            if (pausedMechanism == null)
            {
                Debug.LogWarning("【等待回響】放置目標的路燈未連結機關狀態控制器。", lamp);
                return;
            }
            pausedMechanism.PauseMechanism(this);
            isWaiting = true;
            Debug.Log($"【等待回響】已固定路燈目前狀態：{lamp.name}。", this);
            return;
        }

        heldDoor = EchoInitialPlacement.FindDoor(placementTarget);
        pausedMechanism = heldDoor == null
            ? EchoInitialPlacement.FindMechanism(placementTarget)
            : null;

        if (heldDoor != null)
        {
            heldDoor.HoldDoor(this);
            isWaiting = true;
            Debug.Log($"【等待回想】已固定解謎門：{heldDoor.name}。", this);
            return;
        }

        if (pausedMechanism == null)
        {
            Debug.LogWarning("【等待回響】放置位置附近找不到可作用的機關。", this);
            return;
        }

        pausedMechanism.PauseMechanism(this);
        isWaiting = true;
        Debug.Log($"【等待回響】已作用於機關：{pausedMechanism.name}。", this);
    }

}
