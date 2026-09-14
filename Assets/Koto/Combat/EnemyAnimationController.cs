using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>自行處理光照顯示與召喚；根據移動事件播放走路及翻轉，提供攻擊動畫播放入口。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
[AddComponentMenu("Koto/Combat/敵人動畫控制")]
public sealed class EnemyAnimationController : MonoBehaviour
{
    [Header("受光召喚動畫")]
    [InspectorName("實體召喚動畫狀態名稱")]
    [Tooltip("影紋首次受到光線照射時，實體要播放的完整 Animator 狀態名稱。Enemy2 預設為 Base Layer.call。")]
    [SerializeField] private string entitySummonAnimationStateName = "Base Layer.call";

    [InspectorName("實體召喚動畫片段")]
    [Tooltip("放入 Enemy2 的 call 動畫片段；未指定時會依狀態名稱自動尋找。")]
    [SerializeField] private AnimationClip entitySummonAnimationClip = null;

    [InspectorName("實體召喚後待機動畫狀態名稱")]
    [Tooltip("實體召喚動畫結束且影紋仍受光時要播放的完整 Animator 狀態名稱。Enemy2 預設為 Base Layer.stand with moster。")]
    [SerializeField] private string entitySummonedIdleAnimationStateName = "Base Layer.stand with moster";

    [InspectorName("影紋召喚動畫狀態名稱")]
    [Tooltip("影紋首次受到光線照射時要播放的完整 Animator 狀態名稱。moster2 預設為 Base Layer.call。")]
    [SerializeField] private string bodySummonAnimationStateName = "Base Layer.call";

    [InspectorName("影紋召喚動畫片段")]
    [Tooltip("放入 moster2 的 call 動畫片段；未指定時會依狀態名稱自動尋找。")]
    [SerializeField] private AnimationClip bodySummonAnimationClip = null;

    [Header("實體攻擊骨架")]
    [InspectorName("實體攻擊動畫控制器")]
    [Tooltip("放入實體實際播放攻擊動畫的 Animator。")]
    [SerializeField, FormerlySerializedAs("attackAnimator")] private Animator entityAttackAnimator = null;

    [InspectorName("實體待機動畫狀態名稱")]
    [Tooltip("實體未移動、未攻擊時要播放的完整 Animator 狀態名稱。")]
    [SerializeField] private string entityIdleAnimationStateName = "Base Layer.stand";

    [InspectorName("實體移動動畫狀態名稱")]
    [Tooltip("實體實際移動時要播放的完整 Animator 狀態名稱。")]
    [SerializeField] private string entityWalkAnimationStateName = "Base Layer.walk";

    [InspectorName("實體攻擊動畫狀態名稱")]
    [Tooltip("實體要播放的完整攻擊 Animator 狀態名稱。")]
    [SerializeField, FormerlySerializedAs("attackAnimationStateName")] private string entityAttackAnimationStateName = "Base Layer.attack";

    [InspectorName("實體攻擊動畫片段")]
    [Tooltip("放入實體的攻擊動畫片段，用來計算攻擊與骨架命中區間。")]
    [SerializeField, FormerlySerializedAs("attackAnimationClip")] private AnimationClip entityAttackAnimationClip = null;

    [Header("影紋／本體攻擊骨架")]
    [InspectorName("影紋攻擊動畫控制器")]
    [Tooltip("放入影紋／本體實際播放攻擊動畫的 Animator。僅在影紋受光顯示時使用。")]
    [SerializeField] private Animator bodyAttackAnimator = null;

    [InspectorName("影紋待機動畫狀態名稱")]
    [Tooltip("影紋顯示但未攻擊時要播放的完整 Animator 狀態名稱。")]
    [SerializeField] private string bodyIdleAnimationStateName = "Base Layer.stand";

    [InspectorName("影紋攻擊動畫狀態名稱")]
    [Tooltip("影紋／本體要播放的完整攻擊 Animator 狀態名稱。")]
    [SerializeField] private string bodyAttackAnimationStateName = "Base Layer.attack1";

    [InspectorName("影紋攻擊動畫片段")]
    [Tooltip("放入影紋／本體的攻擊動畫片段，用來計算攻擊與骨架命中區間。")]
    [SerializeField] private AnimationClip bodyAttackAnimationClip = null;

    [Header("整體視覺翻轉")]
    [InspectorName("視覺整體")]
    [Tooltip("放入同時包含實體、影紋與兩者骨架的共同父物件。移動時只翻轉此物件，不翻轉碰撞與移動根物件。")]
    [SerializeField] private Transform visualRoot = null;

    [InspectorName("預設圖片朝右")]
    [Tooltip("勾選代表視覺整體 X 縮放為正值時面向右方；若素材左右相反可取消勾選。")]
    [SerializeField] private bool visualFacesRightWhenScaleXPositive = true;

    private const float DefaultAttackAnimationDuration = 0.5833333f;

    private bool isEntitySummoning;

    private bool isBodySummoning;

    private bool bodyWasVisible;

    private bool hasEntityAnimationState;

    private bool hasBodyAnimationState;

    private int entityAnimationStateHash;

    private int bodyAnimationStateHash;

    private Vector3 visualRootOriginalScale;

    private Coroutine entitySummonRoutine;

    private Coroutine bodySummonRoutine;
    private EnemyStateHealth state;
    private EnemyPlayerAttackController combat;
    private EnemyAreaMovement movement;
    private GameObject entity => state != null ? state.Entity : null;
    private GameObject body => state != null ? state.Body : null;
    private bool isEntityAttacking => combat != null && combat.IsEntityAttacking;
    private bool isBodyAttacking => combat != null && combat.IsBodyAttacking;
    public bool IsSummoning => isActiveAndEnabled && IsAnySummonInProgress();
    public bool IsEntitySummoning => isEntitySummoning;
    public bool IsBodySummoning => isBodySummoning;
    public float EntityAttackDuration => GetAnimationDuration(entityAttackAnimationClip);
    public float BodyAttackDuration => GetAnimationDuration(bodyAttackAnimationClip);

    private void OnEnable() => Bind();

    private void Start()
    {
        Bind();
        if (state != null) state.Initialize();
        ResolveAttackAnimationReferences();
        if (visualRoot == null) visualRoot = transform;
        visualRootOriginalScale = visualRoot.localScale;
        UpdateBodyVisibility();
        SetEntityLocomotionAnimation(false);
        SetBodyIdleAnimation();
    }

    private void Bind()
    {
        Unbind();
        state = GetComponent<EnemyStateHealth>();
        combat = GetComponent<EnemyPlayerAttackController>();
        movement = GetComponent<EnemyAreaMovement>();
        if (state != null)
        {
            state.CombatInterrupted += OnCombatInterrupted;
            state.Revived += OnRevived;
        }
        if (movement != null) movement.Moved += OnMoved;
    }

    private void Unbind()
    {
        if (state != null)
        {
            state.CombatInterrupted -= OnCombatInterrupted;
            state.Revived -= OnRevived;
        }
        if (movement != null) movement.Moved -= OnMoved;
    }

    private void Update() => UpdateBodyVisibility();

    private void OnMoved(Vector3 displacement)
    {
        UpdateVisualFacing(displacement);
        SetEntityLocomotionAnimation(displacement.sqrMagnitude > 0.0001f);
    }

    private void OnCombatInterrupted()
    {
        CancelAllSummons(false);
        SetActive(body, false);
        bodyWasVisible = false;
        hasBodyAnimationState = false;
    }

    private void OnRevived()
    {
        UpdateBodyVisibility();
        SetEntityLocomotionAnimation(false);
    }

    private void OnDisable()
    {
        Unbind();
        CancelAllSummons(false);
        combat?.CancelAllAttacks(false);
        SetActive(body, false);
        hasEntityAnimationState = false;
        hasBodyAnimationState = false;
        bodyWasVisible = false;
    }

    public bool TryPlayEntityAttack()
    {
        return !IsSummoning && PlayEntityAnimatorState(entityAttackAnimationStateName, "實體攻擊", true, true);
    }

    public bool TryPlayBodyAttack()
    {
        return !IsSummoning && PlayBodyAnimatorState(bodyAttackAnimationStateName, "影紋攻擊", true, true);
    }

    private void UpdateVisualFacing(Vector3 actualDelta)
    {
        // 只依實際 X 軸位移翻轉；停止或僅 Z 軸移動時保留既有面向，避免左右搖擺。
        if (visualRoot == null || Mathf.Abs(actualDelta.x) < 0.0001f)
        {
            return;
        }

        bool movingRight = actualDelta.x > 0f;
        bool usePositiveScaleX = movingRight == visualFacesRightWhenScaleXPositive;
        float scaleMagnitude = Mathf.Abs(visualRootOriginalScale.x);
        visualRoot.localScale = new Vector3(
            usePositiveScaleX ? scaleMagnitude : -scaleMagnitude,
            visualRootOriginalScale.y,
            visualRootOriginalScale.z);
    }

    private bool IsAnySummonInProgress()
    {
        return isEntitySummoning || isBodySummoning;
    }

    private void StartSummonAnimations()
    {
        // 光線剛照到時，中斷既有揮擊，讓兩個 call 動畫能從第 0 秒完整播放。
        combat?.CancelAllAttacks(false);

        bool entitySummonStarted = PlayEntityAnimatorState(
            entitySummonAnimationStateName,
            "實體召喚",
            true,
            true);
        if (entitySummonStarted)
        {
            isEntitySummoning = true;
            entitySummonRoutine = StartCoroutine(EntitySummonRoutine());
        }

        bool bodySummonStarted = PlayBodyAnimatorState(
            bodySummonAnimationStateName,
            "影紋召喚",
            true,
            true);
        if (bodySummonStarted)
        {
            isBodySummoning = true;
            bodySummonRoutine = StartCoroutine(BodySummonRoutine());
        }

        if (entitySummonStarted || bodySummonStarted)
        {
            Debug.Log("【敵人】受到光線照射，已播放實體與影紋的召喚動畫。", this);
            return;
        }

        // 未配置 Animator 時仍保留原本的受光顯示行為。
        SetEntityLocomotionAnimation(false);
        SetBodyIdleAnimation();
    }

    private IEnumerator EntitySummonRoutine()
    {
        yield return new WaitForSeconds(GetAnimationDuration(entitySummonAnimationClip));

        isEntitySummoning = false;
        entitySummonRoutine = null;
        if (body != null && body.activeInHierarchy && state != null && !state.IsFriendly &&
            state.BodyCanAppear)
        {
            SetEntityLocomotionAnimation(false);
        }
    }

    private IEnumerator BodySummonRoutine()
    {
        yield return new WaitForSeconds(GetAnimationDuration(bodySummonAnimationClip));

        isBodySummoning = false;
        bodySummonRoutine = null;
        if (body != null && body.activeInHierarchy && state != null && !state.IsFriendly &&
            state.BodyCanAppear)
        {
            SetBodyIdleAnimation();
        }
    }

    private void CancelAllSummons(bool restoreAnimations = true)
    {
        CancelEntitySummon(restoreAnimations);
        CancelBodySummon(restoreAnimations);
    }

    private void CancelEntitySummon(bool restoreAnimation)
    {
        bool wasSummoning = isEntitySummoning || entitySummonRoutine != null;
        if (entitySummonRoutine != null)
        {
            StopCoroutine(entitySummonRoutine);
            entitySummonRoutine = null;
        }

        isEntitySummoning = false;
        if (restoreAnimation && wasSummoning)
        {
            SetEntityLocomotionAnimation(false);
        }
    }

    private void CancelBodySummon(bool restoreAnimation)
    {
        bool wasSummoning = isBodySummoning || bodySummonRoutine != null;
        if (bodySummonRoutine != null)
        {
            StopCoroutine(bodySummonRoutine);
            bodySummonRoutine = null;
        }

        isBodySummoning = false;
        if (restoreAnimation && wasSummoning)
        {
            SetBodyIdleAnimation();
        }
    }

    private void ResolveAttackAnimationReferences()
    {
        if (entityAttackAnimator == null && entity != null)
        {
            // 只嘗試實體物件本身，避免誤抓到身後影紋的 Animator。
            entityAttackAnimator = entity.GetComponent<Animator>();
        }

        if (bodyAttackAnimator == null && body != null)
        {
            bodyAttackAnimator = body.GetComponent<Animator>();
        }

        if (entityAttackAnimationClip == null)
        {
            entityAttackAnimationClip = FindAnimatorClip(entityAttackAnimator, entityAttackAnimationStateName);
        }

        if (entitySummonAnimationClip == null)
        {
            entitySummonAnimationClip = FindAnimatorClip(entityAttackAnimator, entitySummonAnimationStateName);
        }

        if (bodyAttackAnimationClip == null)
        {
            bodyAttackAnimationClip = FindAnimatorClip(bodyAttackAnimator, bodyAttackAnimationStateName);
        }

        if (bodySummonAnimationClip == null)
        {
            bodySummonAnimationClip = FindAnimatorClip(bodyAttackAnimator, bodySummonAnimationStateName);
        }
    }

    private static AnimationClip FindAnimatorClip(Animator animator, string stateName)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrWhiteSpace(stateName))
        {
            return null;
        }

        int separatorIndex = stateName.LastIndexOf('.');
        string clipName = separatorIndex >= 0 ? stateName[(separatorIndex + 1)..] : stateName;
        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null && clip.name == clipName)
            {
                return clip;
            }
        }

        return null;
    }

    private static float GetAnimationDuration(AnimationClip attackAnimationClip)
    {
        return attackAnimationClip != null
            ? Mathf.Max(0.01f, attackAnimationClip.length)
            : DefaultAttackAnimationDuration;
    }

    private bool PlayEntityAnimatorState(
        string stateName,
        string animationLabel,
        bool restart,
        bool warnWhenMissing)
    {
        return PlayAnimatorState(
            entityAttackAnimator,
            stateName,
            animationLabel,
            restart,
            warnWhenMissing,
            ref entityAnimationStateHash,
            ref hasEntityAnimationState);
    }

    private bool PlayBodyAnimatorState(
        string stateName,
        string animationLabel,
        bool restart,
        bool warnWhenMissing)
    {
        return PlayAnimatorState(
            bodyAttackAnimator,
            stateName,
            animationLabel,
            restart,
            warnWhenMissing,
            ref bodyAnimationStateHash,
            ref hasBodyAnimationState);
    }

    private bool PlayAnimatorState(
        Animator animator,
        string stateName,
        string animationLabel,
        bool restart,
        bool warnWhenMissing,
        ref int lastStateHash,
        ref bool hasLastState)
    {
        if (!isActiveAndEnabled || animator == null || !animator.isActiveAndEnabled || string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, stateHash))
        {
            if (warnWhenMissing)
            {
                Debug.LogWarning($"【敵人】找不到{animationLabel}狀態「{stateName}」，請確認對應 Animator 的狀態名稱。", this);
            }

            return false;
        }

        // 只在目標狀態改變時切換；不再因 Animator 正在轉場而每幀從 0 秒重播 walk。
        if (!restart && hasLastState && lastStateHash == stateHash)
        {
            return true;
        }

        animator.Play(stateHash, 0, 0f);
        lastStateHash = stateHash;
        hasLastState = true;
        return true;
    }

    public void SetEntityLocomotionAnimation(bool isMoving)
    {
        if (isEntityAttacking || isEntitySummoning || entityAttackAnimator == null)
        {
            return;
        }

        string targetStateName = isMoving
            ? entityWalkAnimationStateName
            : GetEntityIdleAnimationStateName();
        PlayEntityAnimatorState(targetStateName, "實體待機／移動", false, false);
    }

    private string GetEntityIdleAnimationStateName()
    {
        if (body != null && body.activeInHierarchy &&
            !string.IsNullOrWhiteSpace(entitySummonedIdleAnimationStateName))
        {
            return entitySummonedIdleAnimationStateName;
        }

        return entityIdleAnimationStateName;
    }

    public void SetBodyIdleAnimation()
    {
        if (isBodyAttacking || isBodySummoning || body == null || !body.activeInHierarchy || bodyAttackAnimator == null)
        {
            return;
        }

        PlayBodyAnimatorState(bodyIdleAnimationStateName, "影紋待機", false, false);
    }

    private void UpdateBodyVisibility()
    {
        if (body == null)
        {
            return;
        }

        bool shouldBeVisible = state != null && !state.IsFriendly && state.BodyCanAppear;
        if (!shouldBeVisible)
        {
            bool wasVisible = bodyWasVisible || body.activeSelf;
            combat?.CancelBodyAttack(false);
            CancelAllSummons(false);
            SetActive(body, false);
            bodyWasVisible = false;
            hasBodyAnimationState = false;

            if (wasVisible)
            {
                Debug.Log("【敵人】已離開光線照射範圍，影紋本體已隱藏。", this);
            }

            return;
        }

        // 僅在「未受光 → 受光」的瞬間啟動一次，不能在每幀把 call 覆蓋成 stand。
        if (!bodyWasVisible || !body.activeSelf)
        {
            SetActive(body, true);
            bodyWasVisible = true;
            hasBodyAnimationState = false;
            StartSummonAnimations();
            return;
        }

        SetBodyIdleAnimation();
    }

    private static void SetActive(GameObject targetObject, bool active)
    {
        if (targetObject != null && targetObject.activeSelf != active)
        {
            targetObject.SetActive(active);
        }
    }
}
