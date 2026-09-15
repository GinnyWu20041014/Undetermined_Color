using UnityEngine;

/// <summary>
/// 單一路燈控制器：直接監聽指定機關，不需設定 Tag。
/// </summary>
public class StreetLampController : MonoBehaviour
{
    [Header("機關連結")]
    [InspectorName("目標機關狀態控制器")]
    [Tooltip("此路燈只會接收這一個機關的運行、停止與等待暫停狀態。")]
    [SerializeField] private MechanismStateController mechanismStateController = null;

    [Header("路燈圖片")]
    [InspectorName("燈光圖片物件")]
    [Tooltip("放入獨立的燈光圖片物件，不要放路燈柱本身。")]
    [SerializeField] private GameObject lightImageObject = null;

    [Header("故障模式")]
    [InspectorName("啟用故障模式")]
    [SerializeField] private bool faultMode = false;

    [InspectorName("閃爍間隔")]
    [Tooltip("故障模式每次亮／滅切換的間隔秒數。")]
    [Min(0.01f)]
    [SerializeField] private float flickerInterval = 1.5f;

    private bool isPaused;
    private bool previousFaultMode;
    private bool restoreFaultModeAfterWait;
    private float nextFaultToggleTime;

    /// <summary>供路燈解謎判斷此燈是否需要額外比較亮／滅畫面。</summary>
    public bool IsFaultMode => faultMode;

    /// <summary>故障模式目前是否仍在運作並切換運行／停止狀態。</summary>
    public bool IsFaultModeActive => faultMode && !isPaused;

    /// <summary>原本是故障燈，且目前已由等待回響暫時關閉故障模式。</summary>
    public bool IsFaultModeTemporarilyDisabled => restoreFaultModeAfterWait && isPaused && !faultMode;

    /// <summary>等待回響作用期間只暫停故障狀態切換，不改掉故障設定或目前機關狀態。</summary>
    public bool IsFaultCyclePaused => IsFaultModeTemporarilyDisabled;

    /// <summary>供路燈解謎讀取目前燈光圖片是亮起或關閉。</summary>
    public bool IsLightVisible => IsLightImageVisible();

    private void Awake()
    {
        previousFaultMode = faultMode;
    }

    private void OnEnable()
    {
        if (mechanismStateController == null)
        {
            return;
        }

        mechanismStateController.StateChanged += HandleMechanismStateChanged;
        HandleMechanismStateChanged(mechanismStateController.CurrentState, mechanismStateController.IsPaused);
        if (faultMode && !isPaused)
        {
            nextFaultToggleTime = Time.time + FlickerInterval;
        }
    }

    private void OnDisable()
    {
        if (mechanismStateController != null)
        {
            mechanismStateController.StateChanged -= HandleMechanismStateChanged;
        }
    }

    private void Update()
    {
        HandleFaultModeChanged();

        if (mechanismStateController == null || !faultMode || isPaused)
        {
            return;
        }

        if (Time.time < nextFaultToggleTime)
        {
            return;
        }

        MechanismOperationState nextState = mechanismStateController.CurrentState == MechanismOperationState.運行狀態
            ? MechanismOperationState.停止狀態
            : MechanismOperationState.運行狀態;
        mechanismStateController.SetMechanismState(nextState);
        nextFaultToggleTime = Time.time + FlickerInterval;
    }

    private void HandleMechanismStateChanged(MechanismOperationState state, bool paused)
    {
        bool wasPaused = isPaused;
        isPaused = paused;
        SetLightImageVisible(state == MechanismOperationState.運行狀態);

        if (!wasPaused && isPaused)
        {
            restoreFaultModeAfterWait = faultMode;
            if (restoreFaultModeAfterWait)
            {
                // 暫存原設定並實際取消故障模式；CurrentState 不會因此被改變。
                faultMode = false;
                previousFaultMode = false;
                Debug.Log($"【路燈】等待回響已放置，故障狀態已暫時關閉，目前維持：{state}。", this);
            }
            return;
        }

        if (wasPaused && !isPaused)
        {
            bool shouldRestoreFaultMode = restoreFaultModeAfterWait;
            restoreFaultModeAfterWait = false;
            if (shouldRestoreFaultMode)
            {
                faultMode = true;
                previousFaultMode = true;
                nextFaultToggleTime = Time.time + FlickerInterval;
                Debug.Log($"【路燈】等待回響已取回，故障狀態已恢復，目前狀態：{state}。", this);
            }
        }
    }

    private void HandleFaultModeChanged()
    {
        if (previousFaultMode == faultMode)
        {
            return;
        }

        previousFaultMode = faultMode;

        if (faultMode && !isPaused)
        {
            nextFaultToggleTime = Time.time + FlickerInterval;
        }
        else if (mechanismStateController != null)
        {
            SetLightImageVisible(
                mechanismStateController.CurrentState == MechanismOperationState.運行狀態);
        }
    }

    private float FlickerInterval => Mathf.Max(0.01f, flickerInterval);

    private bool IsLightImageVisible()
    {
        return lightImageObject != null && lightImageObject.activeSelf;
    }

    private void SetLightImageVisible(bool isVisible)
    {
        if (lightImageObject != null && lightImageObject.activeSelf != isVisible)
        {
            lightImageObject.SetActive(isVisible);
        }
    }
}
