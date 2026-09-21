using UnityEngine;

/// <summary>停止回想：放置後永久固定單一機關的當下狀態，並立即消耗。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Echo/停止回想")]
public sealed class StopEcho : MonoBehaviour, IItemPlacementListener
{
    [Header("機關搜尋")]
    [InspectorName("機關作用範圍")]
    [Tooltip("放置目標沒有雙門機關時，在此範圍內尋找最近的可停止機關。")]
    [SerializeField, Min(0.01f)] private float mechanismSearchRange = 2f;

    private bool hasBeenUsed;

    public void OnItemPlaced(GameObject placementTarget)
    {
        if (hasBeenUsed)
        {
            return;
        }

        Vector3 placementPosition = placementTarget != null
            ? placementTarget.transform.position
            : transform.position;
        DualDoorPuzzleController doorMechanism = FindDoorMechanism(placementPosition, placementTarget);
        MechanismStateController legacyMechanism = doorMechanism == null
            ? FindTargetMechanism(placementPosition, placementTarget)
            : null;
        if (doorMechanism == null && legacyMechanism == null)
        {
            Debug.LogWarning("【停止回想】放置位置附近找不到可停止的機關。", this);
            return;
        }

        hasBeenUsed = true;
        string targetName;
        if (doorMechanism != null)
        {
            doorMechanism.StopMechanismPermanently(this);
            targetName = doorMechanism.name;
        }
        else
        {
            legacyMechanism.StopMechanismPermanently(this);
            targetName = legacyMechanism.name;
        }

        Debug.Log($"【停止回想】已永久停止機關：{targetName}，回想已消失。", this);
        gameObject.SetActive(false);
    }

    private DualDoorPuzzleController FindDoorMechanism(Vector3 position, GameObject placementTarget)
    {
        DualDoorPuzzleController target = FindDoorMechanismOnTarget(placementTarget);
        if (target != null)
        {
            return target;
        }

        float range = Mathf.Max(0.01f, mechanismSearchRange);
        float nearestDistanceSquared = range * range;
        DualDoorPuzzleController nearest = null;
        foreach (DualDoorPuzzleController mechanism in
                 FindObjectsByType<DualDoorPuzzleController>(FindObjectsSortMode.None))
        {
            Vector3 offset = mechanism.transform.position - position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = mechanism;
            }
        }

        return nearest;
    }

    private static DualDoorPuzzleController FindDoorMechanismOnTarget(GameObject placementTarget)
    {
        if (placementTarget == null)
        {
            return null;
        }

        DualDoorPuzzleController controller = placementTarget.GetComponent<DualDoorPuzzleController>();
        return controller != null
            ? controller
            : placementTarget.GetComponentInParent<DualDoorPuzzleController>();
    }

    public void OnItemPickedUp()
    {
        // 停止回想只在放置成功時生效，且使用後無法取回。
    }

    private MechanismStateController FindTargetMechanism(Vector3 position, GameObject placementTarget)
    {
        MechanismStateController target = FindOnPlacementTarget(placementTarget);
        if (target != null)
        {
            return target;
        }

        float nearestDistanceSquared = Mathf.Max(0.01f, mechanismSearchRange);
        nearestDistanceSquared *= nearestDistanceSquared;
        MechanismStateController nearest = null;
        foreach (MechanismStateController mechanism in
                 FindObjectsByType<MechanismStateController>(FindObjectsSortMode.None))
        {
            Vector3 offset = mechanism.transform.position - position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = mechanism;
            }
        }

        return nearest;
    }

    private static MechanismStateController FindOnPlacementTarget(GameObject placementTarget)
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
}
