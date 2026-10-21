using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

/// <summary>Monster3-2 主控制器：整合實體／本體血量、受光顯隱、玩家偵測、追蹤、避障、擊退與 NPC 遊蕩；動畫由獨立元件處理。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-300)]
[AddComponentMenu("Koto/Combat/Monster3-2 怪物控制（血量／偵測／移動）")]
public sealed class Monster32StateHealth : MonoBehaviour
{
    [Header("玩家偵測與保持距離")]
    [InspectorName("偵測玩家")]
    [SerializeField] private Transform player = null;
    [InspectorName("玩家偵測半徑")]
    [SerializeField, Min(0.01f)] private float playerDetectionRange = 8f;
    [InspectorName("與玩家最小距離")]
    [SerializeField, Min(0.1f)] private float minimumPlayerDistance = 1.5f;
    [InspectorName("無光源時保持距離")]
    [SerializeField, Min(0.1f)] private float fallbackDistance = 3.5f;
    [InspectorName("光照邊緣內縮距離")]
    [Tooltip("停在玩家光源可照射範圍內，預留此距離避免在邊界反覆顯隱。")]
    [SerializeField, Min(0.01f)] private float lightRangeMargin = 0.35f;
    [InspectorName("距離容許誤差")]
    [SerializeField, Min(0.01f)] private float distanceTolerance = 0.15f;
    [Header("預留近身攻擊點（荊棘不使用）")]
    [InspectorName("實體攻擊點")]
    [SerializeField] private Transform entityAttackPoint = null;
    [InspectorName("本體攻擊點")]
    [SerializeField] private Transform bodyAttackPoint = null;


    [Header("移動設定")]
    [InspectorName("移動速度")]
    [Tooltip("敵人追蹤玩家或遊蕩時在 X/Z 平面上的移動速度。")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;

    [Header("受擊擊退")]
    [SerializeField, Min(0f)] private float knockbackDistance = 0.8f;
    [SerializeField, Min(0.01f)] private float knockbackDuration = 0.2f;
    private Vector3 knockbackDirection;
    private float knockbackElapsed;
    private bool isKnockedBack;
    public bool IsKnockedBack => isKnockedBack;

    public void ApplyKnockback(Vector3 attackerPosition)
    {
        knockbackDirection = Vector3.ProjectOnPlane(transform.position - attackerPosition, Vector3.up).normalized;
        if (knockbackDirection.sqrMagnitude < 0.0001f) knockbackDirection = Vector3.back;
        knockbackElapsed = 0f;
        isKnockedBack = knockbackDistance > 0f;
        if (isKnockedBack)
        {
            ResetWanderTarget();

            // 受擊當下就停止行走動畫，不等下一次移動更新。
            Moved?.Invoke(Vector3.zero);
        }
    }

    [Header("障礙偵測")]
    [InspectorName("避障半徑")]
    [SerializeField, Min(0.05f)] private float obstacleRadius = 0.35f;
    [InspectorName("避障高度")]
    [SerializeField, Min(0.1f)] private float obstacleHeight = 1.5f;
    [InspectorName("地板偵測上下距離")]
    [Tooltip("以角色位置向上及向下偵測 floor 的距離。地板須有 3D Collider。")]
    [SerializeField, Min(0.1f)] private float floorProbeDistance = 2f;
    [InspectorName("遊蕩選點距離")]
    [SerializeField, Min(0.1f)] private float wanderDistance = 4f;

    private readonly List<Collider> floorColliders = new List<Collider>();
    private readonly RaycastHit[] obstacleHits = new RaycastHit[32];
    private readonly Collider[] obstacleOverlaps = new Collider[32];
    private float nextFloorRefresh;
    private float avoidanceSide = 1f;

    [Header("遊蕩待機")]
    [InspectorName("最短待機時間")]
    [SerializeField, Min(0f)] private float minWanderIdleDuration = 2f;

    [InspectorName("最長待機時間")]
    [Tooltip("怪物與 NPC 抵達遊蕩目標後，會在最短與最長時間之間隨機待機，再選取下一個目標。")]
    [SerializeField, Min(0f)] private float maxWanderIdleDuration = 3f;

    private float wanderIdleUntil;

    private bool hasFriendlyMoveTarget;

    private Vector3 friendlyMoveTarget;

    private Monster32AnimationController animations;
    private Monster3ThornAttack thornAttack;
    public Transform FacingTarget { get; private set; }
    public Vector3? ChasePosition { get; private set; }
    public Transform EntityAttackPoint => entityAttackPoint;
    public Transform BodyAttackPoint => bodyAttackPoint;
    public Vector3 FrameDisplacement { get; private set; }
    public event Action<Vector3> Moved;

    public enum EnemyStatus
    {
        敵對狀態,
        友好狀態
    }

    [Header("狀態與數值")]
    [InspectorName("目前狀態")]
    [Tooltip("敵對狀態偵測與追蹤玩家；本體顯示且召喚結束後施放荊棘，本體被擊敗才切換 NPC。")]
    [SerializeField] private EnemyStatus status = EnemyStatus.敵對狀態;

    [InspectorName("實體最大血量")]
    [Tooltip("實體最大血量；只有實體復活時回復。")]
    [SerializeField, Min(1), FormerlySerializedAs("health")] private int entityMaxHealth = 100;

    [InspectorName("本體最大血量")]
    [SerializeField, Min(1)] private int bodyMaxHealth = 100;

    [InspectorName("攻擊力（預留）")]
    [Tooltip("實體與影紋本體的每次攻擊點命中傷害。")]
    [SerializeField, Min(0)] private int attackPower = 10;

    [InspectorName("實體復活時間")]
    [Tooltip("實體血量歸零後，在原地等待多久才復活。")]
    [SerializeField, Min(0f)] private float reviveDelay = 3f;

    [Header("玩家攻擊受擊範圍")]
    [InspectorName("敵人實體")]
    [Tooltip("放入敵人實體物件；玩家會以圓形距離判定是否命中。")]
    [SerializeField] private GameObject entity = null;

    [InspectorName("實體受擊半徑")]
    [Tooltip("以實體為中心的圓形受擊範圍半徑。")]
    [SerializeField, Min(0.01f)] private float entityHitRadius = 0.75f;

    [InspectorName("NPC 物件")]
    [Tooltip("放入友好狀態時顯示的 NPC GameObject 或 Prefab。場景物件建議放在怪物根物件下；Prefab 會在轉為友好時建立。")]
    [SerializeField] private GameObject normalNpc = null;

    [Header("怪物本體與光源")]
    [InspectorName("怪物本體")]
    [Tooltip("指定固定的 monster3-2 本體。enemy3 實體受玩家光照時顯示，失光 3 秒後隱藏，血量不重置。")]
    [SerializeField] private GameObject body = null;

    [InspectorName("本體受擊半徑")]
    [Tooltip("以本體為中心的圓形受擊範圍半徑；本體顯示期間才會生效。")]
    [SerializeField, Min(0.01f)] private float bodyHitRadius = 0.75f;

    [InspectorName("偵測光源")]
    [Tooltip("只指定玩家光源。以 enemy3 實體位置判定距離與聚光燈角度，不以本體位置判定。")]
    [SerializeField] private Light[] lightSources = System.Array.Empty<Light>();

    [Header("執行中血量（開始遊戲時初始化）")]
    [InspectorName("實體目前血量")]
    [SerializeField] private int entityHealth;

    [InspectorName("本體目前血量")]
    [SerializeField] private int bodyHealth;

    private bool entityDefeated;

    private Vector3 revivePosition;

    private GameObject npcInstance;
    private readonly Dictionary<Renderer, bool> entityRendererStates = new Dictionary<Renderer, bool>();

    private bool initialized;
    private float entityReviveAt;
    private const float BodyLightLossDelay = 3f;
    private bool bodyHasBeenIlluminated;
    private float bodyLightLostAt = float.PositiveInfinity;

    public int AttackPower => attackPower;
    public EnemyStatus Status => status;
    public bool IsFriendly => status == EnemyStatus.友好狀態;
    public bool IsEntityDefeated => entityDefeated;
    public GameObject Entity => entity;
    public GameObject Body => body;
    /// <summary>目前實際顯示的 NPC，包含從 Prefab 建立的實例。</summary>
    public GameObject NpcInstance => npcInstance;
    public bool BodyCanAppear => isActiveAndEnabled && !IsFriendly && !entityDefeated &&
        bodyHasBeenIlluminated && Time.time < bodyLightLostAt + BodyLightLossDelay;
    public bool BodyCanBeHit => BodyCanAppear && body != null && body.activeInHierarchy;

    public event Action CombatInterrupted;
    public event Action BecameFriendly;
    public event Action Revived;

    private void Awake()
    {
        animations = GetComponent<Monster32AnimationController>();
        thornAttack = GetComponent<Monster3ThornAttack>();
        avoidanceSide = Random.value < 0.5f ? -1f : 1f;
        Initialize();
    }

    private void Update()
    {
        if (entityDefeated && !IsFriendly && Time.time >= entityReviveAt)
        {
            transform.position = revivePosition;
            entityHealth = entityMaxHealth;
            entityDefeated = false;
            Revived?.Invoke();
        }
        UpdateBodyIllumination();
        // 血量元件本身即可控制顯隱，不必另外掛動畫元件。
        SetActive(body, BodyCanAppear);
        UpdateDetection();
        UpdateMovement();
    }

    private void LateUpdate()
    {
        // 等動畫元件完成本幀的召喚狀態更新，再判斷是否可以施放。
        if (!BodyCanBeHit || FacingTarget == null || IsKnockedBack ||
            (animations != null && animations.IsSummoning)) return;
        if (thornAttack == null || !thornAttack.Ready) return;
        PlayerHealth target = FacingTarget.GetComponentInParent<PlayerHealth>();
        if (target == null || !target.gameObject.activeInHierarchy || target.IsDead) return;
        thornAttack.Release(target);
    }

    private void OnDisable()
    {
        ResetBodyIllumination();
        SetActive(body, false);
        FacingTarget = null;
        ChasePosition = null;
        isKnockedBack = false;
        ResetWanderTarget();
        FrameDisplacement = Vector3.zero;
        Moved?.Invoke(FrameDisplacement);
    }

    private bool detachedBody;
    private void OnDestroy()
    {
        if (detachedBody && body != null) Destroy(body);
    }

    public int EntityHealth => entityHealth;
    public int BodyHealth => bodyHealth;
    public Light[] PlayerLights => lightSources;

    private void UpdateBodyIllumination()
    {
        if (IsFriendly || entityDefeated || body == null)
        {
            ResetBodyIllumination();
            return;
        }

        if (IsBodyIlluminated())
        {
            bodyHasBeenIlluminated = true;
            bodyLightLostAt = float.PositiveInfinity;
        }
        else if (bodyHasBeenIlluminated && float.IsPositiveInfinity(bodyLightLostAt))
        {
            bodyLightLostAt = Time.time;
        }
    }

    private void ResetBodyIllumination()
    {
        bodyHasBeenIlluminated = false;
        bodyLightLostAt = float.PositiveInfinity;
    }

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        if (entity == null) entity = gameObject;
        if (body != null && (body == entity || body == gameObject || transform.IsChildOf(body.transform)))
        {
            Debug.LogError("【Monster3-2】本體必須是獨立物件，不能指定實體、控制器或其父物件。", this);
            body = null;
        }
        // 本體必須獨立於移動／翻轉階層；保留場景初始世界位置與縮放。
        if (body != null && body != gameObject && body.transform.IsChildOf(transform))
        {
            body.transform.SetParent(null, true);
            detachedBody = true;
        }
        entityHealth = entityMaxHealth;
        bodyHealth = bodyMaxHealth;
        if (normalNpc != null && normalNpc.scene.IsValid() &&
            (normalNpc == entity || transform.IsChildOf(normalNpc.transform)))
        {
            Debug.LogWarning("【敵人】NPC 物件請指定獨立的外觀物件，不能使用怪物實體、控制器本身或其父物件。", this);
            normalNpc = null;
        }
        if (entity != null)
        {
            foreach (Renderer renderer in entity.GetComponentsInChildren<Renderer>(true))
                entityRendererStates[renderer] = renderer.enabled;
        }
        ApplyStatusVisuals();
        UpdateBodyIllumination();
        SetActive(body, BodyCanAppear);
    }

    public void TakeDamage(int damage)
    {
        Initialize();
        if (!isActiveAndEnabled || IsFriendly || damage <= 0 || entityHealth <= 0 || entityDefeated) return;
        entityHealth = Mathf.Max(0, entityHealth - damage);
        Debug.Log($"【敵人】實體受到 {damage} 點傷害，目前血量：{entityHealth}。", this);
        if (entityHealth != 0) return;

        entityDefeated = true;
        revivePosition = transform.position;
        entityReviveAt = Time.time + Mathf.Max(0f, reviveDelay);
        ResetBodyIllumination();
        SetActive(body, false);
        CombatInterrupted?.Invoke();
    }

    public bool TryReceivePlayerAttack(int damage, Vector3 attackPosition, float playerAttackRadius, Vector3? attackerPosition = null)
    {
        Initialize();
        if (!isActiveAndEnabled || damage <= 0 || IsFriendly || entityDefeated) return false;
        if (BodyCanBeHit && IsInsideHitRange(body.transform.position, bodyHitRadius, attackPosition, playerAttackRadius))
        {
            AttackFeedbackController.NotifyPlayerHit(body, gameObject);
            TakeBodyDamage(damage);
            return true;
        }
        if (entity != null && entity.activeInHierarchy &&
            IsInsideHitRange(entity.transform.position, entityHitRadius, attackPosition, playerAttackRadius))
        {
            AttackFeedbackController.NotifyPlayerHit(entity, gameObject);
            TakeDamage(damage);
            if (!entityDefeated)
                ApplyKnockback(attackerPosition ?? attackPosition);
            return true;
        }
        return false;
    }

    private void TakeBodyDamage(int damage)
    {
        if (damage <= 0 || bodyHealth <= 0 || IsFriendly) return;
        bodyHealth = Mathf.Max(0, bodyHealth - damage);
        Debug.Log($"【敵人】本體受到 {damage} 點傷害，目前血量：{bodyHealth}。", this);
        if (bodyHealth == 0) BecomeFriendly();
    }

    private void BecomeFriendly()
    {
        isKnockedBack = false;
        ResetWanderTarget();
        status = EnemyStatus.友好狀態;
        ResetBodyIllumination();
        entityDefeated = false;
        CombatInterrupted?.Invoke();
        ApplyStatusVisuals();
        SetActive(body, false);
        BecameFriendly?.Invoke();
        Debug.Log("【敵人】本體血量歸零，已切換友好狀態並顯示 NPC 物件。", this);
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

    private void ApplyStatusVisuals()
    {
        bool isFriendly = status == EnemyStatus.友好狀態;
        if (normalNpc != null && npcInstance == null)
        {
            if (normalNpc.scene.IsValid())
                npcInstance = normalNpc;
            else if (isFriendly)
            {
                // 只建立執行中的實例，不直接啟用或修改 Prefab 資產。
                Transform anchor = entity != null ? entity.transform : transform;
                Transform parent = anchor.parent != null && anchor.IsChildOf(transform) ? anchor.parent : transform;
                npcInstance = Instantiate(normalNpc, anchor.position, anchor.rotation, parent);
            }
        }

        foreach (var entry in entityRendererStates)
        {
            Renderer renderer = entry.Key;
            if (renderer == null) continue;
            // NPC 可以位於實體階層下；只隱藏怪物外觀，保留其控制腳本與 NPC。
            if (npcInstance != null && renderer.transform.IsChildOf(npcInstance.transform)) continue;
            renderer.enabled = entry.Value && !(isFriendly && npcInstance != null);
        }
        SetActive(npcInstance, isFriendly);

        if (isFriendly)
        {
            SetActive(body, false);
        }
    }

    private bool IsBodyIlluminated()
    {
        if (entity == null)
        {
            return false;
        }

        foreach (Light lightSource in lightSources)
        {
            if (lightSource == null || !lightSource.isActiveAndEnabled || lightSource.intensity <= 0f || (lightSource.cullingMask & (1 << entity.layer)) == 0)
            {
                continue;
            }

            if (lightSource.type == LightType.Directional)
            {
                return true;
            }

            Vector3 offset = entity.transform.position - lightSource.transform.position;
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

    private void UpdateDetection()
    {
        FacingTarget = null;
        ChasePosition = null;
        if (IsFriendly || IsEntityDefeated ||
            player == null || !player.gameObject.activeInHierarchy ||
            IsKnockedBack) return;
        PlayerHealth playerHealth = player.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null && playerHealth.IsDead) return;
        Vector3 offset = Vector3.ProjectOnPlane(transform.position - player.position, Vector3.up);
        if (offset.sqrMagnitude > playerDetectionRange * playerDetectionRange) return;
        FacingTarget = player;
        animations?.FaceTarget(player);
        if (animations != null && animations.IsSummoning) return;

        Vector3 center = player.position;
        float desiredDistance = fallbackDistance;
        float bestRange = 0f;
        foreach (Light source in PlayerLights)
        {
            if (source == null || !source.isActiveAndEnabled || source.intensity <= 0f ||
                (source.cullingMask & (1 << Entity.layer)) == 0 ||
                (source.type != LightType.Point && source.type != LightType.Spot)) continue;
            float height = source.transform.position.y - Entity.transform.position.y;
            float horizontalRangeSquared = source.range * source.range - height * height;
            if (horizontalRangeSquared <= 0f) continue;
            float range = Mathf.Max(0.1f, Mathf.Sqrt(horizontalRangeSquared) - lightRangeMargin);
            if (range <= bestRange) continue;
            bestRange = range;
            desiredDistance = range;
            center = source.transform.position;
        }
        // 光源可能比偵測半徑更大，停靠點仍須留在偵測範圍，避免進出邊界反覆追蹤。
        float maximumDistance = Mathf.Max(0.1f, playerDetectionRange - distanceTolerance - lightRangeMargin);
        float minimumDistance = Mathf.Min(minimumPlayerDistance, maximumDistance);
        desiredDistance = Mathf.Clamp(desiredDistance, minimumDistance, maximumDistance);
        offset = Vector3.ProjectOnPlane(transform.position - center, Vector3.up);
        Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.back;
        Vector3 destination = center + direction * desiredDistance;
        destination.y = transform.position.y;
        // 光源不一定就在玩家中心，另行限制與玩家的距離。
        Vector3 fromPlayer = Vector3.ProjectOnPlane(destination - player.position, Vector3.up);
        float distanceFromPlayer = Mathf.Clamp(fromPlayer.magnitude, minimumDistance, maximumDistance);
        Vector3 playerDirection = fromPlayer.sqrMagnitude > 0.0001f ? fromPlayer.normalized : direction;
        destination = player.position + playerDirection * distanceFromPlayer;
        destination.y = transform.position.y;
        if (Vector3.Distance(transform.position, destination) <= distanceTolerance) return;
        ChasePosition = destination;
    }


    private void ResetWanderTarget()
    {
        hasFriendlyMoveTarget = false;
        wanderIdleUntil = 0f;
    }

    private void UpdateMovement()
    {
        RefreshFloors();
        FrameDisplacement = Vector3.zero;
        if (IsEntityDefeated) isKnockedBack = false;
        if (isKnockedBack)
        {
            float duration = Mathf.Max(0.01f, knockbackDuration);
            float before = Mathf.Clamp01(knockbackElapsed / duration);
            knockbackElapsed += Time.deltaTime;
            float after = Mathf.Clamp01(knockbackElapsed / duration);
            float distance = knockbackDistance * ((1f - before) * (1f - before) - (1f - after) * (1f - after));
            Vector3 displacement = knockbackDirection * distance;
            if (CanMove(displacement))
            {
                FrameDisplacement = displacement;
                transform.position += displacement;
            }
            // 即使撞牆無法位移，也等待擊退時間結束才恢復走動。
            // 通知動畫時保留擊退旗標，包含最後一幀的位移。
            Moved?.Invoke(FrameDisplacement);
            isKnockedBack = knockbackElapsed < duration;
            return;
        }
        if (!IsEntityDefeated &&
            (animations == null || !animations.IsSummoning))
        {
            if (IsFriendly)
                WanderInsideAreas();
            else
            {
                // 追蹤期間取消遊蕩待機。
                ResetWanderTarget();
                if (ChasePosition.HasValue)
                    MoveOnXZ(ChasePosition.Value);
            }
        }
        Moved?.Invoke(FrameDisplacement);
    }

    private void RefreshFloors()
    {
        if (Time.time < nextFloorRefresh) return;
        nextFloorRefresh = Time.time + 1f;
        floorColliders.Clear();
        foreach (GameObject floor in GameObject.FindGameObjectsWithTag("floor"))
            foreach (Collider collider in floor.GetComponents<Collider>())
                if (IsValidFloor(collider)) floorColliders.Add(collider);
    }

    private void MoveOnXZ(Vector3 targetPosition)
    {
        Vector3 offset = Vector3.ProjectOnPlane(targetPosition - transform.position, Vector3.up);
        float distance = Mathf.Min(offset.magnitude, Mathf.Max(0f, moveSpeed) * Time.deltaTime);
        if (distance <= 0f) return;
        Vector3 displacement = offset.normalized * distance;
        if (!CanMove(displacement))
        {
            hasFriendlyMoveTarget = false;
            // 保持相同繞行側，避免遇牆時每幀左右抖動。
            bool found = false;
            for (int angle = 45; angle <= 180 && !found; angle += 45)
            {
                for (int side = 0; side < 2; side++)
                {
                    float turn = angle * avoidanceSide * (side == 0 ? 1f : -1f);
                    Vector3 candidate = Quaternion.AngleAxis(turn, Vector3.up) * displacement;
                    if (!CanMove(candidate)) continue;
                    displacement = candidate;
                    found = true;
                    break;
                }
            }
            if (!found) return;
        }
        transform.position += displacement;
        FrameDisplacement = displacement;
    }

    private void WanderInsideAreas()
    {
        if (Time.time < wanderIdleUntil) return;
        if (!hasFriendlyMoveTarget)
        {
            if (!TryChooseWanderTarget())
            {
                // 被包圍時稍後再試，避免每幀反覆搜尋。
                wanderIdleUntil = Time.time + 0.3f;
                return;
            }
        }
        MoveOnXZ(friendlyMoveTarget);
        Vector3 offset = Vector3.ProjectOnPlane(friendlyMoveTarget - transform.position, Vector3.up);
        if (hasFriendlyMoveTarget && offset.sqrMagnitude <= 0.05f * 0.05f)
        {
            hasFriendlyMoveTarget = false;
            float minimum = Mathf.Max(0f, minWanderIdleDuration);
            float maximum = Mathf.Max(minimum, maxWanderIdleDuration);
            wanderIdleUntil = Time.time + Random.Range(minimum, maximum);
        }
    }

    private bool TryChooseWanderTarget()
    {
        float range = Mathf.Max(0.1f, wanderDistance);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector2 direction = Random.insideUnitCircle.normalized;
            float distance = Random.Range(Mathf.Min(0.5f, range), range);
            Vector3 offset = new Vector3(direction.x, 0f, direction.y) * distance;
            if (offset.sqrMagnitude < 0.01f || !CanMove(offset)) continue;
            friendlyMoveTarget = transform.position + offset;
            hasFriendlyMoveTarget = true;
            return true;
        }
        return false;
    }

    private bool CanMove(Vector3 displacement)
    {
        float distance = displacement.magnitude;
        if (distance < 0.00001f) return false;
        float radius = Mathf.Max(0.05f, obstacleRadius);
        // 檢查整段路徑及腳下範圍，避免跨過地板間的空隙。
        int samples = Mathf.Max(1, Mathf.CeilToInt(distance / (radius * 0.5f)));
        for (int index = 1; index <= samples; index++)
        {
            Vector3 point = transform.position + displacement * ((float)index / samples);
            if (!HasFloor(point) || !HasFloor(point + Vector3.right * radius) ||
                !HasFloor(point - Vector3.right * radius) || !HasFloor(point + Vector3.forward * radius) ||
                !HasFloor(point - Vector3.forward * radius)) return false;
        }

        Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
        Vector3 top = transform.position + Vector3.up * (Mathf.Max(obstacleHeight, radius * 2f) - radius + 0.05f);
        int hits = Physics.CapsuleCastNonAlloc(bottom, top, radius, displacement / distance,
            obstacleHits, distance + 0.02f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits == obstacleHits.Length) return false;
        for (int index = 0; index < hits; index++)
            if (IsObstacle(obstacleHits[index].collider)) return false;

        int overlaps = Physics.OverlapCapsuleNonAlloc(bottom + displacement, top + displacement,
            radius, obstacleOverlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (overlaps == obstacleOverlaps.Length) return false;
        for (int index = 0; index < overlaps; index++)
            if (IsObstacle(obstacleOverlaps[index])) return false;
        return true;
    }

    private bool HasFloor(Vector3 position)
    {
        float probe = Mathf.Max(0.1f, floorProbeDistance);
        Ray ray = new Ray(position + Vector3.up * probe, Vector3.down);
        foreach (Collider floor in floorColliders)
            if (IsValidFloor(floor) && floor.Raycast(ray, out RaycastHit hit, probe * 2f) && hit.normal.y > 0.3f)
                return true;
        return false;
    }

    private bool IsObstacle(Collider collider)
    {
        return collider != null && !collider.transform.IsChildOf(transform) && !IsValidFloor(collider);
    }

    private static bool IsValidFloor(Collider collider)
    {
        return collider != null && collider.enabled && !collider.isTrigger &&
            collider.gameObject.activeInHierarchy && collider.CompareTag("floor");
    }
}
