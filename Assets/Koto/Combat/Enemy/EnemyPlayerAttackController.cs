using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>自行偵測玩家及管理雙方攻擊時機。只發布追蹤／停留意圖，位置由區域移動元件更新。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[AddComponentMenu("Koto/Combat/敵人偵測玩家與攻擊控制")]
public sealed class EnemyPlayerAttackController : MonoBehaviour
{
    [Header("玩家偵測與移動")]
    [InspectorName("偵測玩家")]
    [Tooltip("要被敵人偵測的玩家 Transform。")]
    [SerializeField] private Transform player = null;

    [InspectorName("玩家偵測半徑")]
    [Tooltip("以敵人為中心，在 X/Z 平面向外擴散的圓形偵測範圍。")]
    [SerializeField, Min(0.01f)] private float playerDetectionRange = 8f;


    [InspectorName("實體攻擊點")]
    [Tooltip("指定敵人底下的空物件，作為攻擊中心；未指定時使用敵人根物件位置。請勿綁定動畫骨架。")]
    [SerializeField] private Transform entityAttackPoint = null;

    [InspectorName("實體攻擊點半徑")]
    [Tooltip("每個實體攻擊點周圍的 X/Z 平面命中厚度，不是敵人整體的攻擊範圍。")]
    [SerializeField, Min(0.01f), FormerlySerializedAs("attackHitRadius")] [FormerlySerializedAs("entityBoneHitRadius")] private float entityAttackPointRadius = 0.5f;

    [InspectorName("實體命中開始進度")]
    [Tooltip("實體攻擊動畫從哪個進度開始啟用攻擊點傷害。")]
    [SerializeField, Range(0f, 1f)] private float entityHitWindowStart = 0.2f;

    [InspectorName("實體命中結束進度")]
    [Tooltip("實體攻擊動畫到哪個進度關閉攻擊點傷害。")]
    [SerializeField, Range(0f, 1f)] private float entityHitWindowEnd = 0.4f;

    [InspectorName("影紋攻擊點")]
    [Tooltip("指定敵人底下的空物件，作為攻擊中心；未指定時使用敵人根物件位置。請勿綁定動畫骨架。")]
    [SerializeField] private Transform bodyAttackPoint = null;

    [InspectorName("影紋攻擊點半徑")]
    [Tooltip("每個影紋攻擊點周圍的 X/Z 平面命中厚度，不是影紋整體的攻擊範圍。")]
    [SerializeField, Min(0.01f)] [FormerlySerializedAs("bodyBoneHitRadius")] private float bodyAttackPointRadius = 0.5f;

    [InspectorName("影紋命中開始進度")]
    [Tooltip("影紋攻擊動畫從哪個進度開始啟用攻擊點傷害。")]
    [SerializeField, Range(0f, 1f)] private float bodyHitWindowStart = 0.2f;

    [InspectorName("影紋命中結束進度")]
    [Tooltip("影紋攻擊動畫到哪個進度關閉攻擊點傷害。")]
    [SerializeField, Range(0f, 1f)] private float bodyHitWindowEnd = 0.4f;


    private Transform EntityAttackPoint => entityAttackPoint != null ? entityAttackPoint : transform;
    private Transform BodyAttackPoint => bodyAttackPoint != null ? bodyAttackPoint : transform;
    private bool isEntityAttacking;

    private bool isBodyAttacking;

    private bool isEntityHitWindowOpen;

    private bool isBodyHitWindowOpen;

    private bool entityAttackHitApplied;

    private bool bodyAttackHitApplied;

    private bool hasWarnedMissingEntitySetup;

    private bool hasWarnedMissingBodySetup;

    private Coroutine entityAttackRoutine;

    private Coroutine bodyAttackRoutine;
    private EnemyStateHealth state;
    private EnemyAnimationController animations;
    private EnemyAreaMovement movement;
    private Monster3ThornAttack thornAttack;
    private GameObject body => state != null ? state.Body : null;
    private int attackPower => state != null ? state.AttackPower : 0;
    public bool IsEntityAttacking => isEntityAttacking;
    public bool IsBodyAttacking => isBodyAttacking;
    public Transform ChaseTarget { get; private set; }
    public Transform FacingTarget { get; private set; }
    public Vector3? ChasePosition { get; private set; }
    public bool WantsToWander { get; private set; }

    private void OnEnable() => Bind();
    private void Start()
    {
        Bind();
        if (state != null) state.Initialize();
    }

    private void Bind()
    {
        if (state != null) state.CombatInterrupted -= OnCombatInterrupted;
        state = GetComponent<EnemyStateHealth>();
        animations = GetComponent<EnemyAnimationController>();
        movement = GetComponent<EnemyAreaMovement>();
        thornAttack = GetComponent<Monster3ThornAttack>();
        if (state != null) state.CombatInterrupted += OnCombatInterrupted;
    }

    private void OnDisable()
    {
        if (state != null) state.CombatInterrupted -= OnCombatInterrupted;
        CancelAllAttacks();
        ChaseTarget = null;
        ChasePosition = null;
        WantsToWander = false;
    }

    private void OnCombatInterrupted()
    {
        CancelAllAttacks();
        ChaseTarget = null;
        ChasePosition = null;
        WantsToWander = false;
    }

    private void Update()
    {
        FacingTarget = null;
        ChaseTarget = null;
        ChasePosition = null;
        WantsToWander = false;
        if (movement != null && movement.IsKnockedBack)
        {
            CancelAllAttacks();
            return;
        }
        if (state == null || !state.isActiveAndEnabled || state.IsEntityDefeated)
        {
            CancelAllAttacks();
            return;
        }
        if (state.IsFriendly)
        {
            CancelAllAttacks();
            WantsToWander = true;
            return;
        }
        if (!state.BodyCanBeHit) CancelBodyAttack(false);
        if (animations != null && animations.IsSummoning) return;
        if (player == null)
        {
            CancelAllAttacks();
            animations?.SetEntityLocomotionAnimation(false);
            return;
        }

        PlayerHealth playerHealth = GetPlayerHealth();
        if ((playerHealth != null && playerHealth.IsDead) ||
            HorizontalDistance(player.position) > playerDetectionRange)
        {
            CancelAllAttacks();
            WantsToWander = true;
            return;
        }
        FacingTarget = player;
        // 先確定面向，再計算會隨視覺翻轉的攻擊點位置。
        animations?.FaceTarget(player);
        // 荊棘可在偵測範圍內施放；只有施放期間停步，冷卻期間繼續追蹤。
        if (thornAttack != null && thornAttack.isActiveAndEnabled)
        {
            if (state.BodyCanBeHit && thornAttack.Ready && !IsAnyAttackInProgress())
                TryStartBodyAttack();
            if (IsAnyAttackInProgress())
            {
                animations?.SetEntityLocomotionAnimation(false);
                return;
            }
            Transform point = state.BodyCanBeHit ? BodyAttackPoint : EntityAttackPoint;
            float radius = state.BodyCanBeHit ? bodyAttackPointRadius : entityAttackPointRadius;
            if (TryGetAttackApproach(playerHealth, point, radius, out Vector3 thornApproach) &&
                thornApproach.sqrMagnitude > 0.0001f)
            {
                ChaseTarget = player;
                ChasePosition = transform.position + thornApproach;
            }
            return;
        }
        // 揮擊中不因玩家被擊退或短暫移動而取消整次攻擊。
        if (IsAnyAttackInProgress())
        {
            UpdateAttackApproach(playerHealth);
            animations?.SetEntityLocomotionAnimation(false);
            return;
        }
        bool entityReady = TryGetAttackApproach(playerHealth, EntityAttackPoint, entityAttackPointRadius, out Vector3 entityApproach)
            && entityApproach.sqrMagnitude < 0.0001f;
        bool bodyAvailable = state.BodyCanBeHit && body != null && body.activeInHierarchy;
        bool bodyReady = bodyAvailable && TryGetAttackApproach(playerHealth, BodyAttackPoint, bodyAttackPointRadius, out Vector3 bodyApproach)
            && bodyApproach.sqrMagnitude < 0.0001f;
        if (entityReady) TryStartEntityAttack();
        if (bodyReady) TryStartBodyAttack();
        if (!IsAnyAttackInProgress())
        {
            // 先把攻擊點移到能命中的位置，再開始計算攻擊動畫與傷害窗口。
            Vector3 approach;
            if ((bodyAvailable && TryGetAttackApproach(playerHealth, BodyAttackPoint, bodyAttackPointRadius, out approach)) ||
                TryGetAttackApproach(playerHealth, EntityAttackPoint, entityAttackPointRadius, out approach))
            {
                if (approach.sqrMagnitude > 0.0001f)
                {
                    ChaseTarget = player;
                    ChasePosition = transform.position + approach;
                }
            }
            else
            {
                WarnMissingEntityAttackSetup();
            }
        }
        else
        {
            UpdateAttackApproach(playerHealth);
            animations?.SetEntityLocomotionAnimation(false);
        }
    }

    private void UpdateAttackApproach(PlayerHealth playerHealth)
    {
        if (playerHealth == null) return;

        bool useEntity = isEntityAttacking && !entityAttackHitApplied;
        Transform point = useEntity ? EntityAttackPoint : isBodyAttacking && !bodyAttackHitApplied ? BodyAttackPoint : null;
        float radius = useEntity ? entityAttackPointRadius : bodyAttackPointRadius;
        if (point == null) return;
        if (TryGetAttackApproach(playerHealth, point, radius, out Vector3 approach) && approach.sqrMagnitude > 0.0001f)
        {
            ChaseTarget = player;
            ChasePosition = transform.position + approach;
        }
    }

    private static bool TryGetAttackApproach(PlayerHealth playerHealth, Transform point, float radius, out Vector3 approach)
    {
        approach = Vector3.zero;
        if (playerHealth == null || point == null || !point.gameObject.activeInHierarchy) return false;
        Vector3 offset = playerHealth.transform.position - point.position;
        offset.y = 0f;
        float reach = (playerHealth.HitRadius + radius) * 0.85f;
        float gap = offset.magnitude - reach;
        if (gap > 0f) approach = offset.normalized * gap;
        return true;
    }

    private float HorizontalDistance(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void LateUpdate()
    {
        // 移動更新完成後，以攻擊點的世界座標判定命中。
        TryApplyEntityPointHit();
        TryApplyBodyPointHit();
    }

    private PlayerHealth GetPlayerHealth()
    {
        PlayerHealth playerHealth = player.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
        {
            playerHealth = player.gameObject.AddComponent<PlayerHealth>();
        }

        return playerHealth;
    }

    private bool TryStartEntityAttack()
    {
        if (isEntityAttacking || (animations != null && animations.IsEntitySummoning))
        {
            return false;
        }

        if (animations == null || !animations.TryPlayEntityAttack())
        {
            WarnMissingEntityAttackSetup();
            return false;
        }


        entityAttackRoutine = StartCoroutine(EntityAttackRoutine());
        Debug.Log("【敵人】已停止追擊並開始播放實體攻擊動畫。", this);
        return true;
    }

    private bool TryStartBodyAttack()
    {
        if (body == null || !body.activeInHierarchy || (state == null || !state.BodyCanBeHit) || (animations != null && animations.IsBodySummoning))
        {
            return false;
        }

        if (isBodyAttacking || animations == null || !animations.TryPlayBodyAttack())
        {
            WarnMissingBodyAttackSetup();
            return false;
        }


        bodyAttackRoutine = StartCoroutine(BodyAttackRoutine());
        Debug.Log("【敵人】影紋受光顯示，已開始播放影紋攻擊動畫。", this);
        return true;
    }

    private IEnumerator EntityAttackRoutine()
    {
        isEntityAttacking = true;
        entityAttackHitApplied = false;
        yield return RunAttackHitWindow(
            animations.EntityAttackDuration,
            entityHitWindowStart,
            entityHitWindowEnd,
            true);

        isEntityAttacking = false;
        isEntityHitWindowOpen = false;
        entityAttackRoutine = null;
        animations?.SetEntityLocomotionAnimation(false);
    }

    private IEnumerator BodyAttackRoutine()
    {
        isBodyAttacking = true;
        bodyAttackHitApplied = false;
        if (thornAttack != null && thornAttack.isActiveAndEnabled)
            thornAttack.Release(GetPlayerHealth());
        yield return RunAttackHitWindow(
            animations.BodyAttackDuration,
            bodyHitWindowStart,
            bodyHitWindowEnd,
            false);

        isBodyAttacking = false;
        isBodyHitWindowOpen = false;
        bodyAttackRoutine = null;
        animations?.SetBodyIdleAnimation();
    }

    private IEnumerator RunAttackHitWindow(
        float animationDuration,
        float windowStartNormalized,
        float windowEndNormalized,
        bool isEntityAttack)
    {
        float start = Mathf.Clamp01(windowStartNormalized) * animationDuration;
        float end = Mathf.Max(start, Mathf.Clamp01(windowEndNormalized) * animationDuration);

        if (start > 0f)
        {
            yield return new WaitForSeconds(start);
        }

        SetHitWindow(isEntityAttack, true);
        if (end > start)
        {
            yield return new WaitForSeconds(end - start);
        }

        SetHitWindow(isEntityAttack, false);
        if (animationDuration > end)
        {
            yield return new WaitForSeconds(animationDuration - end);
        }
    }

    private void SetHitWindow(bool isEntityAttack, bool isOpen)
    {
        if (isEntityAttack)
        {
            isEntityHitWindowOpen = isOpen;
        }
        else
        {
            isBodyHitWindowOpen = isOpen;
        }
    }

    private void TryApplyEntityPointHit()
    {
        if (!isEntityAttacking || !isEntityHitWindowOpen || entityAttackHitApplied)
        {
            return;
        }

        if (TryApplyPointHit(EntityAttackPoint, entityAttackPointRadius, "實體"))
        {
            entityAttackHitApplied = true;
        }
    }

    private void TryApplyBodyPointHit()
    {
        if (thornAttack != null && thornAttack.isActiveAndEnabled) return;
        if (!isBodyAttacking || !isBodyHitWindowOpen || bodyAttackHitApplied ||
            body == null || !body.activeInHierarchy || (state == null || !state.BodyCanBeHit))
        {
            return;
        }

        if (TryApplyPointHit(BodyAttackPoint, bodyAttackPointRadius, "影紋"))
        {
            bodyAttackHitApplied = true;
        }
    }

    private bool TryApplyPointHit(Transform point, float radius, string attackerName)
    {
        if (player == null || attackPower <= 0 || point == null || !point.gameObject.activeInHierarchy) return false;
        PlayerHealth playerHealth = GetPlayerHealth();
        if (playerHealth == null || playerHealth.IsDead) return false;
        return playerHealth.TryReceiveEnemyAttack(attackPower, point.position, radius, transform.position);
    }

    private bool IsAnyAttackInProgress()
    {
        return isEntityAttacking || isBodyAttacking;
    }

    public void CancelAllAttacks(bool restoreAnimations = true)
    {
        CancelEntityAttack(restoreAnimations);
        CancelBodyAttack(restoreAnimations);
    }

    private void CancelEntityAttack(bool restoreAnimation)
    {
        bool wasAttacking = isEntityAttacking || entityAttackRoutine != null || isEntityHitWindowOpen;
        if (entityAttackRoutine != null)
        {
            StopCoroutine(entityAttackRoutine);
            entityAttackRoutine = null;
        }

        isEntityAttacking = false;
        isEntityHitWindowOpen = false;
        entityAttackHitApplied = false;
        if (restoreAnimation && wasAttacking)
        {
            animations?.SetEntityLocomotionAnimation(false);
        }
    }

    public void CancelBodyAttack(bool restoreAnimation)
    {
        bool wasAttacking = isBodyAttacking || bodyAttackRoutine != null || isBodyHitWindowOpen;
        if (bodyAttackRoutine != null)
        {
            StopCoroutine(bodyAttackRoutine);
            bodyAttackRoutine = null;
        }

        isBodyAttacking = false;
        isBodyHitWindowOpen = false;
        bodyAttackHitApplied = false;
        if (restoreAnimation && wasAttacking)
        {
            animations?.SetBodyIdleAnimation();
        }
    }

    private void WarnMissingEntityAttackSetup()
    {
        if (hasWarnedMissingEntitySetup)
        {
            return;
        }

        Debug.LogWarning("【敵人】請確認實體攻擊動畫設定。", this);
        hasWarnedMissingEntitySetup = true;
    }

    private void WarnMissingBodyAttackSetup()
    {
        if (hasWarnedMissingBodySetup)
        {
            return;
        }

        Debug.LogWarning("【敵人】請確認影紋攻擊動畫設定。", this);
        hasWarnedMissingBodySetup = true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, playerDetectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(EntityAttackPoint.position, entityAttackPointRadius);
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(BodyAttackPoint.position, bodyAttackPointRadius);
    }
}
