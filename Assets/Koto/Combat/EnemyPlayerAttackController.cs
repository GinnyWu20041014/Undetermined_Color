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

    [InspectorName("停止追擊距離")]
    [Tooltip("進入此距離後，仍須讓攻擊骨架接近玩家受擊範圍才會揮擊；距離不足時繼續調整站位。")]
    [SerializeField, Min(0.01f), FormerlySerializedAs("attackRange")] private float stopChasingDistance = 1.5f;

    [InspectorName("實體攻擊骨架")]
    [Tooltip("可增加多個手部、爪子或武器末端骨架。只有這些骨架在命中區間碰到玩家才會造成傷害。")]
    [SerializeField] private Transform[] entityAttackBones = System.Array.Empty<Transform>();

    [InspectorName("實體骨架命中厚度")]
    [Tooltip("每個實體攻擊骨架周圍的 X/Z 平面命中厚度，不是敵人整體的攻擊範圍。")]
    [SerializeField, Min(0.01f), FormerlySerializedAs("attackHitRadius")] private float entityBoneHitRadius = 0.5f;

    [InspectorName("實體命中開始進度")]
    [Tooltip("實體攻擊動畫從哪個進度開始啟用骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float entityHitWindowStart = 0.2f;

    [InspectorName("實體命中結束進度")]
    [Tooltip("實體攻擊動畫到哪個進度關閉骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float entityHitWindowEnd = 0.4f;

    [InspectorName("影紋攻擊骨架")]
    [Tooltip("可增加多個影紋手部、爪子或武器末端骨架。只有這些骨架在命中區間碰到玩家才會造成傷害。")]
    [SerializeField] private Transform[] bodyAttackBones = System.Array.Empty<Transform>();

    [InspectorName("影紋骨架命中厚度")]
    [Tooltip("每個影紋攻擊骨架周圍的 X/Z 平面命中厚度，不是影紋整體的攻擊範圍。")]
    [SerializeField, Min(0.01f)] private float bodyBoneHitRadius = 0.5f;

    [InspectorName("影紋命中開始進度")]
    [Tooltip("影紋攻擊動畫從哪個進度開始啟用骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float bodyHitWindowStart = 0.2f;

    [InspectorName("影紋命中結束進度")]
    [Tooltip("影紋攻擊動畫到哪個進度關閉骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float bodyHitWindowEnd = 0.4f;

    // 保留舊版單一命中點資料，載入後自動轉成第一個實體攻擊骨架。
    [HideInInspector, SerializeField, FormerlySerializedAs("attackHitPoint")] private Transform legacyEntityAttackBone = null;

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
    private GameObject body => state != null ? state.Body : null;
    private int attackPower => state != null ? state.AttackPower : 0;
    public bool IsEntityAttacking => isEntityAttacking;
    public bool IsBodyAttacking => isBodyAttacking;
    public Transform ChaseTarget { get; private set; }
    public Vector3? ChasePosition { get; private set; }
    public bool WantsToWander { get; private set; }

    private void OnEnable() => Bind();
    private void Start()
    {
        Bind();
        if (state != null) state.Initialize();
        PrepareLegacyAttackBone();
    }

    private void Bind()
    {
        if (state != null) state.CombatInterrupted -= OnCombatInterrupted;
        state = GetComponent<EnemyStateHealth>();
        animations = GetComponent<EnemyAnimationController>();
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
        ChaseTarget = null;
        ChasePosition = null;
        WantsToWander = false;
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
        // 揮擊中不因玩家被擊退或短暫移動而取消整次攻擊。
        if (IsAnyAttackInProgress())
        {
            UpdateAttackApproach(playerHealth);
            animations?.SetEntityLocomotionAnimation(false);
            return;
        }
        if (HorizontalDistance(player.position) > stopChasingDistance)
        {
            CancelAllAttacks();
            ChaseTarget = player;
            ChasePosition = player.position;
            return;
        }
        bool entityReady = TryGetAttackApproach(playerHealth, entityAttackBones, entityBoneHitRadius, out Vector3 entityApproach)
            && entityApproach.sqrMagnitude < 0.0001f;
        bool bodyAvailable = state.BodyCanBeHit && body != null && body.activeInHierarchy;
        bool bodyReady = bodyAvailable && TryGetAttackApproach(playerHealth, bodyAttackBones, bodyBoneHitRadius, out Vector3 bodyApproach)
            && bodyApproach.sqrMagnitude < 0.0001f;
        if (entityReady) TryStartEntityAttack();
        if (bodyReady) TryStartBodyAttack();
        if (!IsAnyAttackInProgress())
        {
            // 先把可用骨架移到能命中的位置，再開始計算攻擊動畫與傷害窗口。
            if (TryGetAttackApproach(playerHealth, entityAttackBones, entityBoneHitRadius, out Vector3 approach) ||
                (bodyAvailable && TryGetAttackApproach(playerHealth, bodyAttackBones, bodyBoneHitRadius, out approach)))
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

        // 以實體骨架優先決定站位，避免兩組骨架同時拉扯移動方向。
        Transform[] bones = isEntityAttacking && !entityAttackHitApplied && HasAnyValidBone(entityAttackBones)
            ? entityAttackBones : isBodyAttacking && !bodyAttackHitApplied ? bodyAttackBones : null;
        float radius = bones == entityAttackBones ? entityBoneHitRadius : bodyBoneHitRadius;
        if (bones == null) return;

        if (TryGetAttackApproach(playerHealth, bones, radius, out Vector3 approach) && approach.sqrMagnitude > 0.0001f)
        {
            ChaseTarget = player;
            ChasePosition = transform.position + approach;
        }
    }

    private static bool TryGetAttackApproach(PlayerHealth playerHealth, Transform[] bones, float radius, out Vector3 approach)
    {
        approach = Vector3.zero;
        if (playerHealth == null || bones == null) return false;

        Vector3 closestOffset = Vector3.zero;
        float closestDistance = float.PositiveInfinity;
        foreach (Transform bone in bones)
        {
            if (bone == null || !bone.gameObject.activeInHierarchy) continue;
            Vector3 offset = playerHealth.transform.position - bone.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < closestDistance)
            {
                closestOffset = offset;
                closestDistance = offset.sqrMagnitude;
            }
        }
        if (float.IsPositiveInfinity(closestDistance)) return false;

        // 留少量重疊，避免停在浮點誤差或動畫移動造成的命中邊界外。
        float reach = (playerHealth.HitRadius + radius) * 0.85f;
        float gap = Mathf.Sqrt(closestDistance) - reach;
        if (gap > 0f) approach = closestOffset.normalized * gap;
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
        // Animator 已在此時更新完骨架姿勢，再以骨架的實際世界座標判定命中。
        TryApplyEntityBoneHit();
        TryApplyBodyBoneHit();
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

        // 骨架尚未指定時仍允許播放攻擊動畫，只是不會造成命中傷害。
        if (!HasAnyValidBone(entityAttackBones))
        {
            WarnMissingEntityAttackSetup();
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

        // 骨架尚未指定時仍允許播放攻擊動畫，只是不會造成命中傷害。
        if (!HasAnyValidBone(bodyAttackBones))
        {
            WarnMissingBodyAttackSetup();
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

    private void TryApplyEntityBoneHit()
    {
        if (!isEntityAttacking || !isEntityHitWindowOpen || entityAttackHitApplied)
        {
            return;
        }

        if (TryApplyBoneHit(entityAttackBones, entityBoneHitRadius, "實體"))
        {
            entityAttackHitApplied = true;
        }
    }

    private void TryApplyBodyBoneHit()
    {
        if (!isBodyAttacking || !isBodyHitWindowOpen || bodyAttackHitApplied ||
            body == null || !body.activeInHierarchy || (state == null || !state.BodyCanBeHit))
        {
            return;
        }

        if (TryApplyBoneHit(bodyAttackBones, bodyBoneHitRadius, "影紋"))
        {
            bodyAttackHitApplied = true;
        }
    }

    private bool TryApplyBoneHit(Transform[] attackBones, float boneHitRadius, string attackerName)
    {
        if (player == null || attackPower <= 0)
        {
            return false;
        }

        PlayerHealth playerHealth = GetPlayerHealth();
        if (playerHealth == null || playerHealth.IsDead)
        {
            return false;
        }

        foreach (Transform attackBone in attackBones)
        {
            if (attackBone == null || !attackBone.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (playerHealth.TryReceiveEnemyAttack(attackPower, attackBone.position, boneHitRadius, transform.position))
            {
                Debug.Log($"【敵人】{attackerName}攻擊骨架命中玩家，造成 {attackPower} 點傷害。", this);
                return true;
            }
        }

        return false;
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

    private static bool HasAnyValidBone(Transform[] attackBones)
    {
        if (attackBones == null)
        {
            return false;
        }

        foreach (Transform attackBone in attackBones)
        {
            if (attackBone != null)
            {
                return true;
            }
        }

        return false;
    }

    private void WarnMissingEntityAttackSetup()
    {
        if (hasWarnedMissingEntitySetup)
        {
            return;
        }

        Debug.LogWarning("【敵人】請確認實體攻擊骨架與攻擊動畫設定；沒有可用的命中骨架時不會開始攻擊。", this);
        hasWarnedMissingEntitySetup = true;
    }

    private void WarnMissingBodyAttackSetup()
    {
        if (hasWarnedMissingBodySetup)
        {
            return;
        }

        Debug.LogWarning("【敵人】影紋攻擊骨架尚未指定；攻擊動畫會播放，但不會對玩家造成命中傷害。", this);
        hasWarnedMissingBodySetup = true;
    }

    private void PrepareLegacyAttackBone()
    {
        if (HasAnyValidBone(entityAttackBones) || legacyEntityAttackBone == null)
        {
            return;
        }

        entityAttackBones = new[] { legacyEntityAttackBone };
    }

    private static void DrawBoneHitGizmos(Transform[] attackBones, float boneHitRadius, Color color)
    {
        if (attackBones == null)
        {
            return;
        }

        Gizmos.color = color;
        foreach (Transform attackBone in attackBones)
        {
            if (attackBone != null)
            {
                Gizmos.DrawWireSphere(attackBone.position, boneHitRadius);
            }
        }
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, playerDetectionRange);
        DrawBoneHitGizmos(entityAttackBones, entityBoneHitRadius, Color.red);
        DrawBoneHitGizmos(bodyAttackBones, bodyBoneHitRadius, new Color(1f, 0.5f, 0f, 1f));
    }
}
