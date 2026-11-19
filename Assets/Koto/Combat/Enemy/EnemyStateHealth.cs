using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>獨立管理實體／本體血量、復活、友好狀態與 NPC 外觀；以事件通知其他元件。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-300)]
[AddComponentMenu("Koto/Combat/敵人狀態血量")]
public sealed class EnemyStateHealth : MonoBehaviour
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
    [Tooltip("放入身後的影紋／怪物本體；實體受光時持續顯示，失光 3 秒後隱藏。")]
    [SerializeField] private GameObject body = null;

    [InspectorName("本體受擊半徑")]
    [Tooltip("以本體為中心的圓形受擊範圍半徑；本體顯示期間才會生效。")]
    [SerializeField, Min(0.01f)] private float bodyHitRadius = 0.75f;

    [InspectorName("偵測光源")]
    [Tooltip("只指定玩家光源。以 Enemy2 實體位置判定距離與聚光燈角度。")]
    [SerializeField] private Light[] lightSources = System.Array.Empty<Light>();

    private int entityHealth;

    private int bodyHealth;

    private bool entityDefeated;

    private Vector3 revivePosition;

    private GameObject npcInstance;
    private readonly Dictionary<Renderer, bool> entityRendererStates = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, bool> npcRendererStates = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Renderer, bool> bodyRendererStates = new Dictionary<Renderer, bool>();

    private bool initialized;
    private Coroutine reviveRoutine;
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

    // 在第一幀渲染前套用外觀狀態，避免場景中仍啟用的 Prefab 子物件先被看見。
    private void Awake() => Initialize();

    private void Update()
    {
        UpdateBodyIllumination();
        SetRenderersVisible(bodyRendererStates, BodyCanAppear);
    }

    private void OnDisable()
    {
        ResetBodyIllumination();
        SetRenderersVisible(bodyRendererStates, false);
    }

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
        entityHealth = health;
        bodyHealth = health;
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
        CacheAppearanceRenderers(normalNpc, npcRendererStates);
        CacheAppearanceRenderers(body, bodyRendererStates);
        // Keep the Animator hierarchy alive; visibility is controlled through its renderers.
        SetActive(body, true);
        ApplyStatusVisuals();
        UpdateBodyIllumination();
        SetRenderersVisible(bodyRendererStates, BodyCanAppear);
    }

    public void TakeDamage(int damage)
    {
        Initialize();
        if (!isActiveAndEnabled || damage <= 0 || entityHealth <= 0 || entityDefeated) return;
        entityHealth = Mathf.Max(0, entityHealth - damage);
        Debug.Log($"【敵人】實體受到 {damage} 點傷害，目前血量：{entityHealth}。", this);
        if (entityHealth != 0) return;

        entityDefeated = true;
        ResetBodyIllumination();
        SetRenderersVisible(bodyRendererStates, false);
        CombatInterrupted?.Invoke();
        reviveRoutine = StartCoroutine(ReviveEntity());
    }

    public bool TryReceivePlayerAttack(int damage, Vector3 attackPosition, float playerAttackRadius, Vector3? attackerPosition = null)
    {
        Initialize();
        if (!isActiveAndEnabled || damage <= 0 || IsFriendly || entityDefeated) return false;
        if (BodyCanBeHit && IsInsideHitRange(body.transform.position, bodyHitRadius, attackPosition, playerAttackRadius))
        {
            AttackFeedbackController.NotifyPlayerHit(body, gameObject, Mathf.Min(damage, bodyHealth));
            TakeBodyDamage(damage);
            GetComponent<EnemyAreaMovement>()?.ApplyKnockback(attackerPosition ?? attackPosition);
            return true;
        }
        if (entity != null && entity.activeInHierarchy &&
            IsInsideHitRange(entity.transform.position, entityHitRadius, attackPosition, playerAttackRadius))
        {
            AttackFeedbackController.NotifyPlayerHit(entity, gameObject, Mathf.Min(damage, entityHealth));
            TakeDamage(damage);
            if (!entityDefeated)
                GetComponent<EnemyAreaMovement>()?.ApplyKnockback(attackerPosition ?? attackPosition);
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

    private IEnumerator ReviveEntity()
    {
        revivePosition = transform.position;
        SetRenderersVisible(bodyRendererStates, false);
        Debug.Log($"【敵人】實體血量歸零，將在原地等待 {reviveDelay:0.##} 秒後復活。", this);
        yield return new WaitForSeconds(reviveDelay);
        transform.position = revivePosition;
        entityHealth = health;
        entityDefeated = false;
        reviveRoutine = null;
        Revived?.Invoke();
        Debug.Log("【敵人】實體已在原地復活並重新開始偵測玩家。", this);
    }

    private void BecomeFriendly()
    {
        if (reviveRoutine != null)
        {
            StopCoroutine(reviveRoutine);
            reviveRoutine = null;
        }
        status = EnemyStatus.友好狀態;
        ResetBodyIllumination();
        entityDefeated = false;
        CombatInterrupted?.Invoke();
        ApplyStatusVisuals();
        SetRenderersVisible(bodyRendererStates, false);
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
                CacheAppearanceRenderers(npcInstance, npcRendererStates);
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
        // 保持 Animator 所在物件啟用，僅切換 Renderer，避免初始停用後 Animator 無法接續播放。
        SetActive(npcInstance, true);
        SetRenderersVisible(npcRendererStates, isFriendly);

        if (isFriendly)
        {
            SetRenderersVisible(bodyRendererStates, false);
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
            if (lightSource == null || !lightSource.isActiveAndEnabled || lightSource.intensity <= 0f ||
                (lightSource.cullingMask & (1 << entity.layer)) == 0)
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

    private static void CacheAppearanceRenderers(GameObject target, Dictionary<Renderer, bool> states)
    {
        if (target == null) return;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            states[renderer] = renderer.enabled;
    }

    private static void SetRenderersVisible(Dictionary<Renderer, bool> states, bool visible)
    {
        foreach (var entry in states)
        {
            if (entry.Key != null)
                entry.Key.enabled = visible && entry.Value;
        }
    }
    private void OnDrawGizmosSelected()
    {
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
}
