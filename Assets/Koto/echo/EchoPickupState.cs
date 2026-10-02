using UnityEngine;

/// <summary>區分回想在列表中、已放置或已消耗；切換掃描不會重設。</summary>
[DisallowMultipleComponent]
public sealed class EchoPickupState : MonoBehaviour
{
    [SerializeField, HideInInspector] private bool isInInventory;
    [SerializeField, HideInInspector] private bool isConsumed;
    public bool IsInInventory => isInInventory;
    public bool IsConsumed => isConsumed;

    public bool TryPickUp()
    {
        if (IsInInventory || IsConsumed) return false;
        isInInventory = true;
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
