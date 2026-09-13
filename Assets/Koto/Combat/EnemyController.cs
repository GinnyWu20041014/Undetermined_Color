using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 敵人的基礎控制器：處理友好／敵對狀態、遊蕩、追蹤、實體／影紋攻擊與光照顯示。
/// 攻擊傷害只會在攻擊動畫的命中區間內，以實際骨架位置判定玩家是否被碰到。
/// </summary>
public class EnemyController : MonoBehaviour
{
    public enum EnemyStatus
    {
        敵對狀態,
        友好狀態
    }

    [Header("狀態與數值")]
    [InspectorName("目前狀態")]
    [Tooltip("敵對狀態會偵測、追蹤並攻擊玩家；友好狀態只會遊蕩。")]
    [SerializeField] private EnemyStatus status = EnemyStatus.敵對狀態;

    [InspectorName("血量")]
    [Tooltip("敵人實體與影紋本體各自使用此最大血量。")]
    [SerializeField, Min(1)] private int health = 100;

    [InspectorName("攻擊力")]
    [Tooltip("實體與影紋本體的每次骨架命中傷害。")]
    [SerializeField, Min(0)] private int attackPower = 10;

    [InspectorName("實體復活時間")]
    [Tooltip("實體血量歸零後，在原地等待多久才復活。")]
    [SerializeField, Min(0f)] private float reviveDelay = 3f;

    /// <summary>提供其他腳本讀取目前設定的攻擊力。</summary>
    public int AttackPower => attackPower;

    [Header("玩家攻擊受擊範圍")]
    [InspectorName("敵人實體")]
    [Tooltip("放入敵人實體物件；玩家會以圓形距離判定是否命中。")]
    [SerializeField] private GameObject entity = null;

    [InspectorName("實體受擊半徑")]
    [Tooltip("以實體為中心的圓形受擊範圍半徑。")]
    [SerializeField, Min(0.01f)] private float entityHitRadius = 0.75f;

    [InspectorName("NPC 型態")]
    [Tooltip("放入友好狀態時要替換實體顯示的 NPC Sprite。")]
    [SerializeField] private Sprite normalNpcSprite = null;

    // 舊場景的 NPC 物件保留為隱藏參考，避免與新的 NPC Sprite 同時顯示。
    [HideInInspector, SerializeField] private GameObject normalNpc = null;

    [Header("怪物本體與光源")]
    [InspectorName("怪物本體")]
    [Tooltip("放入身後的影紋／怪物本體；僅在受到指定光源照射時顯示。")]
    [SerializeField] private GameObject body = null;

    [InspectorName("本體受擊半徑")]
    [Tooltip("以本體為中心的圓形受擊範圍半徑；本體受光顯示時才會生效。")]
    [SerializeField, Min(0.01f)] private float bodyHitRadius = 0.75f;

    [InspectorName("偵測光源")]
    [Tooltip("可增加多個 Light；本體在任一光源照射範圍內時顯示。")]
    [SerializeField] private Light[] lightSources = System.Array.Empty<Light>();

    [Header("玩家偵測與移動")]
    [InspectorName("偵測玩家")]
    [Tooltip("要被敵人偵測的玩家 Transform。")]
    [SerializeField] private Transform player = null;

    [InspectorName("玩家偵測半徑")]
    [Tooltip("以敵人為中心，在 X/Z 平面向外擴散的圓形偵測範圍。")]
    [SerializeField, Min(0.01f)] private float playerDetectionRange = 8f;

    [InspectorName("停止追擊距離")]
    [Tooltip("只決定何時停止移動並開始揮擊；真正是否造成傷害完全由攻擊骨架接觸玩家判定。")]
    [SerializeField, Min(0.01f), FormerlySerializedAs("attackRange")] private float stopChasingDistance = 1.5f;

    [InspectorName("移動速度")]
    [Tooltip("敵人追蹤玩家或遊蕩時在 X/Z 平面上的移動速度。")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;

    [InspectorName("移動區域")]
    [Tooltip("可增加多個 Collider。敵人不追蹤玩家時，會在這些區域內隨機移動。")]
    [SerializeField] private Collider[] movementAreas = System.Array.Empty<Collider>();

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

    [Header("整體視覺翻轉")]
    [InspectorName("視覺整體")]
    [Tooltip("放入同時包含實體、影紋與兩者骨架的共同父物件。移動時只翻轉此物件，不翻轉碰撞與移動根物件。")]
    [SerializeField] private Transform visualRoot = null;

    [InspectorName("預設圖片朝右")]
    [Tooltip("勾選代表視覺整體 X 縮放為正值時面向右方；若素材左右相反可取消勾選。")]
    [SerializeField] private bool visualFacesRightWhenScaleXPositive = true;

    // 保留舊版單一命中點資料，載入後自動轉成第一個實體攻擊骨架。
    [HideInInspector, SerializeField, FormerlySerializedAs("attackHitPoint")] private Transform legacyEntityAttackBone = null;

    private const float DefaultAttackAnimationDuration = 0.5833333f;
    private const float MovementEpsilon = 0.0001f;

    private int entityHealth;
    private int bodyHealth;
    private bool entityDefeated;
    private bool hasFriendlyMoveTarget;
    private bool isEntityAttacking;
    private bool isBodyAttacking;
    private bool isEntitySummoning;
    private bool isBodySummoning;
    private bool bodyWasVisible;
    private bool isEntityHitWindowOpen;
    private bool isBodyHitWindowOpen;
    private bool entityAttackHitApplied;
    private bool bodyAttackHitApplied;
    private bool hasWarnedMissingEntitySetup;
    private bool hasWarnedMissingBodySetup;
    private bool hasEntityAnimationState;
    private bool hasBodyAnimationState;
    private int entityAnimationStateHash;
    private int bodyAnimationStateHash;
    private Vector3 friendlyMoveTarget;
    private Vector3 revivePosition;
    private Vector3 visualRootOriginalScale;
    private Coroutine entityAttackRoutine;
    private Coroutine bodyAttackRoutine;
    private Coroutine entitySummonRoutine;
    private Coroutine bodySummonRoutine;
    private SpriteRenderer entityRenderer;
    private Sprite entityOriginalSprite;
    private Quaternion entityOriginalLocalRotation;

    private void Awake()
    {
        entityHealth = health;
        bodyHealth = health;
        ResolveAttackAnimationReferences();
        PrepareLegacyAttackBone();
        bodyWasVisible = false;

        if (visualRoot == null)
        {
            visualRoot = transform;
        }

        visualRootOriginalScale = visualRoot.localScale;
        if (entity != null)
        {
            entityRenderer = entity.GetComponent<SpriteRenderer>();
            entityOriginalSprite = entityRenderer != null ? entityRenderer.sprite : null;
            entityOriginalLocalRotation = entity.transform.localRotation;
        }

        SetActive(normalNpc, false);
        ApplyStatusVisuals();
        UpdateBodyVisibility();
        SetEntityLocomotionAnimation(false);
        SetBodyIdleAnimation();
    }

    private void OnDisable()
    {
        CancelAllAttacks(false);
        CancelAllSummons(false);
        hasEntityAnimationState = false;
        hasBodyAnimationState = false;
        bodyWasVisible = false;
    }

    private void Update()
    {
        UpdateBodyVisibility();

        // 召喚動畫播放期間完全鎖住移動與攻擊，避免待機、走路或攻擊覆蓋 call 動畫。
        if (IsAnySummonInProgress())
        {
            return;
        }

        if (entityDefeated)
        {
            return;
        }

        if (status == EnemyStatus.友好狀態)
        {
            CancelAllAttacks();
            WanderInsideAreas();
            return;
        }

        if (player == null)
        {
            CancelAllAttacks();
            SetEntityLocomotionAnimation(false);
            return;
        }

        PlayerHealth playerHealth = GetPlayerHealth();
        if (playerHealth != null && playerHealth.IsDead)
        {
            CancelAllAttacks();
            WanderInsideAreas();
            return;
        }

        float playerDistance = GetHorizontalDistance(player.position);
        if (playerDistance > playerDetectionRange)
        {
            // 玩家離開偵測範圍時，立即取消正在播放的攻擊，再返回移動區域遊蕩。
            CancelAllAttacks();
            WanderInsideAreas();
            return;
        }

        if (playerDistance > stopChasingDistance)
        {
            // 玩家離開停止追擊距離時，取消尚未完成的揮擊並立刻恢復追蹤。
            CancelAllAttacks();
            MoveTowardsPlayer();
            return;
        }

        if (IsAnyAttackInProgress())
        {
            // 攻擊動畫進行中不移動，避免追蹤與攻擊同時發生。
            SetEntityLocomotionAnimation(false);
            return;
        }

        bool entityAttackStarted = TryStartEntityAttack();
        bool bodyAttackStarted = TryStartBodyAttack();
        if (entityAttackStarted || bodyAttackStarted)
        {
            SetEntityLocomotionAnimation(false);
            return;
        }

        // 攻擊骨架或 Animator 尚未設定時保持原地，避免敵人持續穿過玩家。
        SetEntityLocomotionAnimation(false);
    }

    private void LateUpdate()
    {
        // Animator 已在此時更新完骨架姿勢，再以骨架的實際世界座標判定命中。
        TryApplyEntityBoneHit();
        TryApplyBodyBoneHit();
    }

    /// <summary>供其他腳本呼叫以扣除實體血量。</summary>
    public void TakeDamage(int damage)
    {
        if (damage <= 0 || entityHealth <= 0 || entityDefeated)
        {
            return;
        }

        entityHealth = Mathf.Max(0, entityHealth - damage);
        Debug.Log($"【敵人】實體受到 {damage} 點傷害，目前血量：{entityHealth}。", this);
        if (entityHealth == 0)
        {
            CancelAllAttacks();
            StartCoroutine(ReviveEntity());
        }
    }

    /// <summary>使用 X/Z 平面圓形距離判定玩家的攻擊是否命中實體或受光顯示的影紋本體。</summary>
    public bool TryReceivePlayerAttack(int damage, Vector3 attackPosition, float playerAttackRadius)
    {
        if (damage <= 0 || status == EnemyStatus.友好狀態 || entityDefeated)
        {
            return false;
        }

        // 本體在受光顯示時優先接受攻擊。
        if (body != null && body.activeInHierarchy && IsBodyIlluminated() &&
            IsInsideHitRange(body.transform.position, bodyHitRadius, attackPosition, playerAttackRadius))
        {
            TakeBodyDamage(damage);
            return true;
        }

        if (entity != null && entity.activeInHierarchy &&
            IsInsideHitRange(entity.transform.position, entityHitRadius, attackPosition, playerAttackRadius))
        {
            TakeDamage(damage);
            return true;
        }

        return false;
    }

    private void MoveTowardsPlayer()
    {
        Vector3 currentPosition = transform.position;
        Vector3 targetPosition = new Vector3(player.position.x, currentPosition.y, player.position.z);
        MoveOnXZ(targetPosition);
    }

    private void MoveOnXZ(Vector3 targetPosition)
    {
        Vector3 currentPosition = transform.position;
        Vector3 nextPosition = Vector3.MoveTowards(
            currentPosition,
            new Vector3(targetPosition.x, currentPosition.y, targetPosition.z),
            moveSpeed * Time.deltaTime);

        Vector3 actualDelta = nextPosition - currentPosition;
        transform.position = nextPosition;
        UpdateVisualFacing(actualDelta);
        SetEntityLocomotionAnimation(actualDelta.sqrMagnitude > MovementEpsilon);
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
        if (isEntityAttacking || isEntitySummoning)
        {
            return false;
        }

        if (!HasAnyValidBone(entityAttackBones) || !PlayEntityAnimatorState(
                entityAttackAnimationStateName,
                "實體攻擊",
                true,
                true))
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
        if (body == null || !body.activeInHierarchy || !IsBodyIlluminated() || isBodySummoning)
        {
            return false;
        }

        if (isBodyAttacking || !HasAnyValidBone(bodyAttackBones) || !PlayBodyAnimatorState(
                bodyAttackAnimationStateName,
                "影紋攻擊",
                true,
                true))
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
            GetAnimationDuration(entityAttackAnimationClip),
            entityHitWindowStart,
            entityHitWindowEnd,
            true);

        isEntityAttacking = false;
        isEntityHitWindowOpen = false;
        entityAttackRoutine = null;
        SetEntityLocomotionAnimation(false);
    }

    private IEnumerator BodyAttackRoutine()
    {
        isBodyAttacking = true;
        bodyAttackHitApplied = false;
        yield return RunAttackHitWindow(
            GetAnimationDuration(bodyAttackAnimationClip),
            bodyHitWindowStart,
            bodyHitWindowEnd,
            false);

        isBodyAttacking = false;
        isBodyHitWindowOpen = false;
        bodyAttackRoutine = null;
        SetBodyIdleAnimation();
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
            body == null || !body.activeInHierarchy || !IsBodyIlluminated())
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
            if (attackBone == null)
            {
                continue;
            }

            if (playerHealth.TryReceiveEnemyAttack(attackPower, attackBone.position, boneHitRadius))
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

    private void CancelAllAttacks(bool restoreAnimations = true)
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
            SetEntityLocomotionAnimation(false);
        }
    }

    private void CancelBodyAttack(bool restoreAnimation)
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
            SetBodyIdleAnimation();
        }
    }

    private bool IsAnySummonInProgress()
    {
        return isEntitySummoning || isBodySummoning;
    }

    private void StartSummonAnimations()
    {
        // 光線剛照到時，中斷既有揮擊，讓兩個 call 動畫能從第 0 秒完整播放。
        CancelAllAttacks(false);

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
        if (body != null && body.activeInHierarchy && status == EnemyStatus.敵對狀態 &&
            !entityDefeated && IsBodyIlluminated())
        {
            SetEntityLocomotionAnimation(false);
        }
    }

    private IEnumerator BodySummonRoutine()
    {
        yield return new WaitForSeconds(GetAnimationDuration(bodySummonAnimationClip));

        isBodySummoning = false;
        bodySummonRoutine = null;
        if (body != null && body.activeInHierarchy && status == EnemyStatus.敵對狀態 &&
            !entityDefeated && IsBodyIlluminated())
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
        if (animator == null || string.IsNullOrWhiteSpace(stateName))
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

    private void SetEntityLocomotionAnimation(bool isMoving)
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

    private void SetBodyIdleAnimation()
    {
        if (isBodyAttacking || isBodySummoning || body == null || !body.activeInHierarchy || bodyAttackAnimator == null)
        {
            return;
        }

        PlayBodyAnimatorState(bodyIdleAnimationStateName, "影紋待機", false, false);
    }

    private void WarnMissingEntityAttackSetup()
    {
        if (hasWarnedMissingEntitySetup)
        {
            return;
        }

        Debug.LogWarning("【敵人】實體攻擊需要指定 Animator、攻擊狀態與至少一個「實體攻擊骨架」。", this);
        hasWarnedMissingEntitySetup = true;
    }

    private void WarnMissingBodyAttackSetup()
    {
        if (hasWarnedMissingBodySetup)
        {
            return;
        }

        Debug.LogWarning("【敵人】影紋攻擊需要指定 Animator、攻擊狀態與至少一個「影紋攻擊骨架」。", this);
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

    private float GetHorizontalDistance(Vector3 targetPosition)
    {
        Vector3 offset = targetPosition - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private static bool IsInsideHitRange(
        Vector3 targetPosition,
        float targetRadius,
        Vector3 attackPosition,
        float playerAttackRadius)
    {
        Vector3 offset = targetPosition - attackPosition;
        offset.y = 0f;
        float combinedRadius = targetRadius + playerAttackRadius;
        return offset.sqrMagnitude <= combinedRadius * combinedRadius;
    }

    private void TakeBodyDamage(int damage)
    {
        if (damage <= 0 || bodyHealth <= 0 || status == EnemyStatus.友好狀態)
        {
            return;
        }

        bodyHealth = Mathf.Max(0, bodyHealth - damage);
        Debug.Log($"【敵人】本體受到 {damage} 點傷害，目前血量：{bodyHealth}。", this);
        if (bodyHealth == 0)
        {
            BecomeFriendly();
        }
    }

    private IEnumerator ReviveEntity()
    {
        entityDefeated = true;
        CancelAllSummons(false);
        revivePosition = transform.position;
        if (entity != null)
        {
            entity.transform.localRotation = entityOriginalLocalRotation * Quaternion.Euler(0f, 0f, -90f);
        }

        SetActive(body, false);
        bodyWasVisible = false;
        hasBodyAnimationState = false;
        Debug.Log($"【敵人】實體血量歸零，已旋轉 Z 軸 -90°，將在原地等待 {reviveDelay:0.##} 秒後復活。", this);

        yield return new WaitForSeconds(reviveDelay);

        transform.position = revivePosition;
        entityHealth = health;
        entityDefeated = false;
        if (entity != null)
        {
            entity.transform.localRotation = entityOriginalLocalRotation;
        }

        UpdateBodyVisibility();
        SetEntityLocomotionAnimation(false);
        Debug.Log("【敵人】實體已在原地復活，Z 軸角度已恢復並重新開始偵測玩家。", this);
    }

    private void BecomeFriendly()
    {
        CancelAllAttacks();
        CancelAllSummons(false);
        StopAllCoroutines();
        status = EnemyStatus.友好狀態;
        entityDefeated = false;
        hasFriendlyMoveTarget = false;
        if (entity != null)
        {
            entity.transform.localRotation = entityOriginalLocalRotation;
        }

        ApplyStatusVisuals();
        SetActive(body, false);
        bodyWasVisible = false;
        hasBodyAnimationState = false;
        SetEntityLocomotionAnimation(false);
        Debug.Log("【敵人】本體血量歸零，已切換友好狀態並替換為 NPC Sprite。", this);
    }

    private void ApplyStatusVisuals()
    {
        bool isFriendly = status == EnemyStatus.友好狀態;
        SetActive(normalNpc, false);
        if (entityRenderer != null)
        {
            entityRenderer.sprite = isFriendly && normalNpcSprite != null
                ? normalNpcSprite
                : entityOriginalSprite;
        }

        if (isFriendly)
        {
            SetActive(body, false);
        }
    }

    private void WanderInsideAreas()
    {
        Collider nearestArea = GetNearestArea();
        if (nearestArea == null)
        {
            SetEntityLocomotionAnimation(false);
            return;
        }

        if (!IsInsideAnyArea(transform.position))
        {
            SetFriendlyMoveTarget(nearestArea.ClosestPoint(transform.position));
        }
        else if (!hasFriendlyMoveTarget || GetHorizontalDistance(friendlyMoveTarget) <= 0.05f)
        {
            SetFriendlyMoveTarget(GetRandomPointInArea(GetRandomArea()));
        }

        MoveOnXZ(friendlyMoveTarget);
    }

    private void SetFriendlyMoveTarget(Vector3 targetPosition)
    {
        friendlyMoveTarget = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
        hasFriendlyMoveTarget = true;
    }

    private void UpdateBodyVisibility()
    {
        if (body == null)
        {
            return;
        }

        bool shouldBeVisible = status == EnemyStatus.敵對狀態 && !entityDefeated && IsBodyIlluminated();
        if (!shouldBeVisible)
        {
            bool wasVisible = bodyWasVisible || body.activeSelf;
            CancelBodyAttack(false);
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

    private bool IsBodyIlluminated()
    {
        if (body == null)
        {
            return false;
        }

        foreach (Light lightSource in lightSources)
        {
            if (lightSource == null || !lightSource.isActiveAndEnabled)
            {
                continue;
            }

            if (lightSource.type == LightType.Directional)
            {
                return true;
            }

            Vector3 offset = body.transform.position - lightSource.transform.position;
            if (offset.sqrMagnitude > lightSource.range * lightSource.range)
            {
                continue;
            }

            if (lightSource.type != LightType.Spot ||
                Vector3.Angle(lightSource.transform.forward, offset) <= lightSource.spotAngle * 0.5f)
            {
                return true;
            }
        }

        return false;
    }

    private Collider GetNearestArea()
    {
        Collider nearestArea = null;
        float nearestDistance = float.MaxValue;
        foreach (Collider area in movementAreas)
        {
            if (!IsValidArea(area))
            {
                continue;
            }

            float distance = (area.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestArea = area;
            }
        }

        return nearestArea;
    }

    private bool IsInsideAnyArea(Vector3 position)
    {
        foreach (Collider area in movementAreas)
        {
            if (IsValidArea(area) && (area.ClosestPoint(position) - position).sqrMagnitude < 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private Collider GetRandomArea()
    {
        int validAreaCount = 0;
        foreach (Collider area in movementAreas)
        {
            if (IsValidArea(area))
            {
                validAreaCount++;
            }
        }

        if (validAreaCount == 0)
        {
            return null;
        }

        int selectedIndex = Random.Range(0, validAreaCount);
        foreach (Collider area in movementAreas)
        {
            if (IsValidArea(area) && selectedIndex-- == 0)
            {
                return area;
            }
        }

        return null;
    }

    private Vector3 GetRandomPointInArea(Collider area)
    {
        if (area == null)
        {
            return transform.position;
        }

        Bounds bounds = area.bounds;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector3 point = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                transform.position.y,
                Random.Range(bounds.min.z, bounds.max.z));
            if ((area.ClosestPoint(point) - point).sqrMagnitude < 0.0001f)
            {
                return point;
            }
        }

        return new Vector3(bounds.center.x, transform.position.y, bounds.center.z);
    }

    private static bool IsValidArea(Collider area)
    {
        return area != null && area.enabled && area.gameObject.activeInHierarchy;
    }

    private static void SetActive(GameObject targetObject, bool active)
    {
        if (targetObject != null && targetObject.activeSelf != active)
        {
            targetObject.SetActive(active);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, playerDetectionRange);

        DrawBoneHitGizmos(entityAttackBones, entityBoneHitRadius, Color.red);
        DrawBoneHitGizmos(bodyAttackBones, bodyBoneHitRadius, new Color(1f, 0.5f, 0f, 1f));

        if (entity != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(entity.transform.position, entityHitRadius);
        }

        if (body != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(body.transform.position, bodyHitRadius);
        }
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
}
