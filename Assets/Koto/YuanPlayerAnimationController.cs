using UnityEngine;

/// <summary>
/// 將 yuan Animator 的待機、移動與死亡動畫連接至現有玩家系統。
/// 本元件只負責動畫，不處理移動、生命或輸入。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
[AddComponentMenu("Koto/Player/Yuan 玩家動畫控制")]
public sealed class YuanPlayerAnimationController : MonoBehaviour
{
    [Header("功能連結")]
    [InspectorName("Yuan 動畫控制器")]
    [Tooltip("放入 yuan 物件上的 Animator；未指定時會自動尋找。")]
    [SerializeField] private Animator yuanAnimator = null;

    [InspectorName("玩家移動腳本")]
    [Tooltip("放入控制此角色的 PlayerMovement；未指定時會自動尋找父物件與子物件。")]
    [SerializeField] private PlayerMovement playerMovement = null;

    [InspectorName("玩家生命腳本")]
    [Tooltip("放入控制此角色生命的 PlayerHealth；未指定時會自動尋找父物件與子物件。")]
    [SerializeField] private PlayerHealth playerHealth = null;

    [Header("Animator 參數名稱")]
    [InspectorName("移動參數名稱")]
    [Tooltip("yuan Animator 內控制待機與走路的 Bool 參數。")]
    [SerializeField] private string movingParameterName = "isMoving";

    [InspectorName("死亡參數名稱")]
    [Tooltip("yuan Animator 內播放死亡動畫的 Trigger 參數。")]
    [SerializeField] private string deathParameterName = "Die";

    [Header("備用動畫狀態名稱")]
    [InspectorName("待機動畫狀態名稱")]
    [Tooltip("移動參數不存在或玩家復活時使用的完整 Animator 狀態名稱。")]
    [SerializeField] private string idleStateName = "Base Layer.stand";

    [InspectorName("移動動畫狀態名稱")]
    [Tooltip("移動參數不存在時使用的完整 Animator 狀態名稱。")]
    [SerializeField] private string walkStateName = "Base Layer.walk";

    [InspectorName("死亡動畫狀態名稱")]
    [Tooltip("死亡 Trigger 不存在時使用的完整 Animator 狀態名稱。")]
    [SerializeField] private string deathStateName = "Base Layer.die yuan";

    private bool wasDead;
    private bool hasMovementState;
    private bool lastMoving;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();
        wasDead = playerHealth != null && playerHealth.IsDead;

        if (wasDead)
        {
            PlayDeathAnimation();
        }
        else
        {
            UpdateMovementAnimation(true);
        }
    }

    private void Update()
    {
        if (yuanAnimator == null)
        {
            ResolveReferences();
            if (yuanAnimator == null)
            {
                return;
            }
        }

        bool isDead = playerHealth != null && playerHealth.IsDead;
        if (isDead != wasDead)
        {
            wasDead = isDead;
            if (isDead)
            {
                PlayDeathAnimation();
            }
            else
            {
                PlayRespawnAnimation();
            }
        }

        if (!isDead)
        {
            UpdateMovementAnimation(false);
        }
    }

    private void ResolveReferences()
    {
        if (yuanAnimator == null)
        {
            yuanAnimator = GetComponent<Animator>();
            if (yuanAnimator == null)
            {
                yuanAnimator = GetComponentInChildren<Animator>();
            }
        }

        if (playerMovement == null)
        {
            playerMovement = GetComponentInParent<PlayerMovement>();
            if (playerMovement == null)
            {
                playerMovement = GetComponentInChildren<PlayerMovement>();
            }
        }

        if (playerHealth == null)
        {
            playerHealth = GetComponentInParent<PlayerHealth>();
            if (playerHealth == null)
            {
                playerHealth = GetComponentInChildren<PlayerHealth>();
            }
        }
    }

    private void UpdateMovementAnimation(bool force)
    {
        bool isMoving = playerMovement != null && playerMovement.IsMoving;
        if (!force && hasMovementState && lastMoving == isMoving)
        {
            return;
        }

        hasMovementState = true;
        lastMoving = isMoving;

        if (TrySetBool(movingParameterName, isMoving))
        {
            return;
        }

        PlayState(isMoving ? walkStateName : idleStateName, isMoving ? "移動" : "待機");
    }

    private void PlayDeathAnimation()
    {
        TrySetBool(movingParameterName, false);
        hasMovementState = false;

        if (!TrySetTrigger(deathParameterName))
        {
            PlayState(deathStateName, "死亡");
        }

        Debug.Log("【Yuan 動畫】玩家死亡，播放死亡動畫。", this);
    }

    private void PlayRespawnAnimation()
    {
        if (HasParameter(deathParameterName, AnimatorControllerParameterType.Trigger))
        {
            yuanAnimator.ResetTrigger(deathParameterName);
        }

        TrySetBool(movingParameterName, false);
        PlayState(idleStateName, "復活待機");
        hasMovementState = false;
        Debug.Log("【Yuan 動畫】玩家復活，恢復待機動畫。", this);
    }

    private bool TrySetBool(string parameterName, bool value)
    {
        if (!HasParameter(parameterName, AnimatorControllerParameterType.Bool))
        {
            return false;
        }

        yuanAnimator.SetBool(parameterName, value);
        return true;
    }

    private bool TrySetTrigger(string parameterName)
    {
        if (!HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
        {
            return false;
        }

        yuanAnimator.ResetTrigger(parameterName);
        yuanAnimator.SetTrigger(parameterName);
        return true;
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType type)
    {
        if (yuanAnimator == null || yuanAnimator.runtimeAnimatorController == null ||
            string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in yuanAnimator.parameters)
        {
            if (parameter.type == type && parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    private void PlayState(string stateName, string label)
    {
        if (yuanAnimator == null || !yuanAnimator.isActiveAndEnabled || string.IsNullOrWhiteSpace(stateName))
        {
            return;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!yuanAnimator.HasState(0, stateHash))
        {
            Debug.LogWarning($"【Yuan 動畫】找不到{label}狀態「{stateName}」。", this);
            return;
        }

        yuanAnimator.CrossFadeInFixedTime(stateHash, 0.05f, 0, 0f);
    }
}
