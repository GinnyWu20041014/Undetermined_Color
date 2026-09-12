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

    private bool isRunning;
    private bool isPaused;
    private bool isFaultFlickerFrozen;
    private bool frozenLightVisible;
    private bool previousFaultMode;
    private float nextFlickerTime;

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

        if (isPaused)
        {
            SetLightImageVisible(isFaultFlickerFrozen && frozenLightVisible);
            return;
        }

        if (!isRunning)
        {
            SetLightImageVisible(false);
            return;
        }

        if (!faultMode)
        {
            SetLightImageVisible(true);
            return;
        }

        if (Time.time >= nextFlickerTime)
        {
            SetLightImageVisible(!IsLightImageVisible());
            nextFlickerTime = Time.time + FlickerInterval;
        }
    }

    private void HandleMechanismStateChanged(MechanismOperationState state, bool paused)
    {
        bool wasPaused = isPaused;
        bool wasRunning = isRunning;

        isRunning = state == MechanismOperationState.運行狀態;
        isPaused = paused;

        if (!wasPaused && isPaused)
        {
            // 故障燈才凍結當下畫面；一般路燈在等待期間維持熄滅。
            isFaultFlickerFrozen = faultMode && isRunning;
            frozenLightVisible = IsLightImageVisible();
            return;
        }

        if (wasPaused && !isPaused)
        {
            ResumeFromWait();
            return;
        }

        if (!isPaused && (!isRunning || !wasRunning || !faultMode))
        {
            ApplyRunningVisual(!wasRunning);
        }
    }

    private void HandleFaultModeChanged()
    {
        if (previousFaultMode == faultMode)
        {
            return;
        }

        previousFaultMode = faultMode;

        if (!isPaused && isRunning)
        {
            ApplyRunningVisual(faultMode);
        }
    }

    private void ResumeFromWait()
    {
        bool resumeFrozenFlicker = isFaultFlickerFrozen && faultMode && isRunning;
        isFaultFlickerFrozen = false;

        if (resumeFrozenFlicker)
        {
            SetLightImageVisible(frozenLightVisible);
            nextFlickerTime = Time.time + FlickerInterval;
            return;
        }

        ApplyRunningVisual(faultMode);
    }

    private void ApplyRunningVisual(bool startFaultBlink)
    {
        if (!isRunning)
        {
            SetLightImageVisible(false);
            return;
        }

        SetLightImageVisible(true);

        if (faultMode && startFaultBlink)
        {
            nextFlickerTime = Time.time + FlickerInterval;
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
