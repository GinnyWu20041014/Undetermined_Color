using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>區分回想在列表中、已放置或已消耗；切換掃描不會重設。</summary>
[DisallowMultipleComponent]
public sealed class EchoPickupState : MonoBehaviour
{
    [SerializeField, HideInInspector] private bool isInInventory;
    [SerializeField, HideInInspector] private bool isConsumed;
    [SerializeField, HideInInspector] private bool hasBeenPickedUp;
    public bool HasBeenPickedUp => hasBeenPickedUp;
    public bool IsInInventory => isInInventory;
    public bool IsConsumed => isConsumed;

    public bool TryPickUp()
    {
        if (IsInInventory || IsConsumed) return false;
        isInInventory = true;
        hasBeenPickedUp = true;
        return true;
    }

    public void MarkPlaced()
    {
        isInInventory = false;
    }

    public void MarkConsumed()
    {
        isInInventory = false;
        isConsumed = true;
    }

    public static bool ShouldHideFromScan(GameObject target)
    {
        // Scanning also searches inactive objects; state lookup must do the same.
        foreach (EchoPickupState state in target.GetComponentsInParent<EchoPickupState>(true))
            if (state.IsInInventory || state.IsConsumed) return true;
        foreach (EchoPickupState state in target.GetComponentsInChildren<EchoPickupState>(true))
            if (state.IsInInventory || state.IsConsumed) return true;
        foreach (StopEcho echo in target.GetComponentsInParent<StopEcho>(true))
            if (echo.IsConsumed) return true;
        foreach (StopEcho echo in target.GetComponentsInChildren<StopEcho>(true))
            if (echo.IsConsumed) return true;
        return false;
    }
}

/// <summary>開場時包含掃描隱藏的回響；候選機關只取 placementTarget。</summary>
internal static class EchoInitialPlacement
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= InitializeScene;
        SceneManager.sceneLoaded += InitializeScene;
    }

    private static void InitializeScene(Scene scene, LoadSceneMode mode)
    {
        foreach (WaitEcho echo in Resources.FindObjectsOfTypeAll<WaitEcho>())
            if (echo.gameObject.scene == scene && echo.enabled) echo.InitializeNearbyMechanism();
        foreach (StopEcho echo in Resources.FindObjectsOfTypeAll<StopEcho>())
            if (echo.gameObject.scene == scene && echo.enabled) echo.InitializeNearbyMechanism();
    }

    public static GameObject FindNearest(Vector3 position, float range, System.Predicate<GameObject> canAffect)
    {
        GameObject nearest = null;
        float nearestDistance = Mathf.Max(0.01f, range);
        nearestDistance *= nearestDistance;
        foreach (GameObject target in GameObject.FindGameObjectsWithTag("placementTarget"))
        {
            if (!canAffect(target)) continue;
            Vector3 offset = target.transform.position - position;
            offset.y = 0f;
            float distance = offset.sqrMagnitude;
            if (distance > nearestDistance) continue;
            // 同距離時維持單一選擇，避免候選物件順序改變結果。
            if (nearest != null && distance == nearestDistance && target.GetInstanceID() > nearest.GetInstanceID()) continue;
            nearestDistance = distance;
            nearest = target;
        }
        return nearest;
    }

    public static PuzzleDoor FindDoor(GameObject target)
    {
        PuzzleDoor door = target.GetComponentInParent<PuzzleDoor>(true);
        if (door != null) return door;
        foreach (PuzzleDoor candidate in Object.FindObjectsByType<PuzzleDoor>(FindObjectsSortMode.None))
            if (candidate.IsEchoPlacementTarget(target)) return candidate;
        return target.GetComponentInChildren<PuzzleDoor>(true);
    }

    public static MechanismStateController FindMechanism(GameObject target)
    {
        StreetLampController lamp = StreetLampController.FindOnPlacementTarget(target);
        if (lamp != null) return lamp.Mechanism;
        MechanismStateController mechanism = target.GetComponentInParent<MechanismStateController>(true);
        return mechanism != null ? mechanism : target.GetComponentInChildren<MechanismStateController>(true);
    }

    public static DualDoorPuzzleController FindDualDoor(GameObject target)
    {
        DualDoorPuzzleController mechanism = target.GetComponentInParent<DualDoorPuzzleController>(true);
        if (mechanism != null) return mechanism;
        PuzzleDoor door = FindDoor(target);
        if (door != null) return door.GetComponentInParent<DualDoorPuzzleController>(true);
        return target.GetComponentInChildren<DualDoorPuzzleController>(true);
    }
}
