using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 機關可使用的兩種基礎狀態。
/// </summary>
public enum MechanismOperationState
{
    停止狀態,
    運行狀態
}

/// <summary>
/// 單一機關的狀態核心。路燈與回響會直接指定這個元件，不需透過 Tag 搜尋。
/// </summary>
public class MechanismStateController : MonoBehaviour
{
    [Header("機關狀態")]
    [InspectorName("目前狀態")]
    [Tooltip("機關原本的運行或停止狀態。等待回響暫停期間會保留此設定。")]
    [SerializeField] private MechanismOperationState currentState = MechanismOperationState.停止狀態;

    private readonly HashSet<UnityEngine.Object> pauseRequesters = new HashSet<UnityEngine.Object>();

    /// <summary>機關原本設定的運行／停止狀態。</summary>
    public MechanismOperationState CurrentState => currentState;

    /// <summary>機關目前是否被一個或多個等待回響暫停。</summary>
    public bool IsPaused => pauseRequesters.Count > 0;

    /// <summary>機關目前實際套用的狀態；暫停期間視為停止。</summary>
    public MechanismOperationState AppliedState => IsPaused
        ? MechanismOperationState.停止狀態
        : currentState;

    /// <summary>
    /// 狀態變化通知。第一個參數為原本運行／停止設定，第二個參數為是否暫停。
    /// </summary>
    public event Action<MechanismOperationState, bool> StateChanged;

    private void Awake()
    {
        NotifyStateChanged();
    }

    /// <summary>設定機關為運行狀態。</summary>
    public void SetRunningState()
    {
        SetMechanismState(MechanismOperationState.運行狀態);
    }

    /// <summary>設定機關為停止狀態。</summary>
    public void SetStoppedState()
    {
        SetMechanismState(MechanismOperationState.停止狀態);
    }

    /// <summary>設定機關原本狀態並通知已連結的路燈。</summary>
    public void SetMechanismState(MechanismOperationState state)
    {
        currentState = state;
        NotifyStateChanged();

        string message = IsPaused
            ? $"已記錄為：{currentState}，目前仍由等待回響暫停。"
            : $"已切換為：{currentState}。";
        Debug.Log($"【機關狀態】{message}", this);
    }

    /// <summary>由等待回響暫停機關，並保留原本運行／停止狀態。</summary>
    public void PauseMechanism(UnityEngine.Object requester)
    {
        if (requester == null || !pauseRequesters.Add(requester))
        {
            return;
        }

        if (pauseRequesters.Count == 1)
        {
            NotifyStateChanged();
            Debug.Log($"【機關狀態】已由「{requester.name}」暫停，取回回響後會恢復為：{currentState}。", this);
        }
    }

    /// <summary>解除指定等待回響的暫停；全部回響取回後才會恢復。</summary>
    public void ResumeMechanism(UnityEngine.Object requester)
    {
        if (requester == null || !pauseRequesters.Remove(requester))
        {
            return;
        }

        if (!IsPaused)
        {
            NotifyStateChanged();
            Debug.Log($"【機關狀態】等待回響已取回，恢復為：{currentState}。", this);
        }
    }

    /// <summary>再次通知目前狀態。可供 Unity Event 或其他腳本手動呼叫。</summary>
    public void ApplyCurrentState()
    {
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(currentState, IsPaused);
    }
}
