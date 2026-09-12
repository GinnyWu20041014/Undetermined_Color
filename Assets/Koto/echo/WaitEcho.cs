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
    [Tooltip("放下回響後，若放置目標沒有機關控制器，會在此範圍內選取最近的一個機關。")]
    [Min(0.01f)]
    [SerializeField] private float mechanismSearchRange = 2f;

    private MechanismStateController pausedMechanism = null;
    private bool isWaiting;

    /// <summary>由物品放置系統呼叫；會選取放置位置對應的一個機關。</summary>
    public void OnItemPlaced(GameObject placementTarget)
    {
        Vector3 placementPosition = placementTarget != null
            ? placementTarget.transform.position
            : transform.position;
        StartWaitingAt(placementPosition, placementTarget);
    }

    /// <summary>由物品撿取系統呼叫；恢復本回響先前選取的機關。</summary>
    public void OnItemPickedUp()
    {
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

        pausedMechanism = null;
        isWaiting = false;
        Debug.Log("【等待回響】回響已取回，機關已恢復原本狀態。", this);
    }

    private void StartWaitingAt(Vector3 position, GameObject placementTarget)
    {
        if (isWaiting)
        {
            return;
        }

        pausedMechanism = FindTargetMechanism(position, placementTarget);
        if (pausedMechanism == null)
        {
            Debug.LogWarning("【等待回響】放置位置附近找不到可作用的機關。", this);
            return;
        }

        pausedMechanism.PauseMechanism(this);
        isWaiting = true;
        Debug.Log($"【等待回響】已作用於機關：{pausedMechanism.name}。", this);
    }

    private MechanismStateController FindTargetMechanism(Vector3 position, GameObject placementTarget)
    {
        MechanismStateController controllerOnTarget = FindControllerOnPlacementTarget(placementTarget);
        return controllerOnTarget != null
            ? controllerOnTarget
            : FindNearestMechanism(position);
    }

    private static MechanismStateController FindControllerOnPlacementTarget(GameObject placementTarget)
    {
        if (placementTarget == null)
        {
            return null;
        }

        MechanismStateController controller = placementTarget.GetComponent<MechanismStateController>();
        if (controller == null)
        {
            controller = placementTarget.GetComponentInParent<MechanismStateController>();
        }

        return controller != null
            ? controller
            : placementTarget.GetComponentInChildren<MechanismStateController>(true);
    }

    private MechanismStateController FindNearestMechanism(Vector3 position)
    {
        float searchRange = Mathf.Max(0.01f, mechanismSearchRange);
        float nearestDistanceSquared = searchRange * searchRange;
        MechanismStateController nearestMechanism = null;

        foreach (MechanismStateController controller in FindObjectsByType<MechanismStateController>(FindObjectsSortMode.None))
        {
            Vector3 offset = controller.transform.position - position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;

            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestMechanism = controller;
            }
        }

        return nearestMechanism;
    }
}
