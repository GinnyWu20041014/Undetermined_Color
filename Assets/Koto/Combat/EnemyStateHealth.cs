using System;
using System.Collections;
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
    [Tooltip("實體與影紋本體的每次骨架命中傷害。")]
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

    private int entityHealth;

    private int bodyHealth;

    private bool entityDefeated;

    private Vector3 revivePosition;

    private SpriteRenderer entityRenderer;

    private Sprite entityOriginalSprite;

    private bool initialized;
    private Coroutine reviveRoutine;

    public int AttackPower => attackPower;
    public EnemyStatus Status => status;
    public bool IsFriendly => status == EnemyStatus.友好狀態;
    public bool IsEntityDefeated => entityDefeated;
    public GameObject Entity => entity;
    public GameObject Body => body;
    public bool BodyCanAppear => isActiveAndEnabled && !IsFriendly && !entityDefeated && IsBodyIlluminated();
    public bool BodyCanBeHit => BodyCanAppear && body != null && body.activeInHierarchy;

    public event Action CombatInterrupted;
    public event Action BecameFriendly;
    public event Action Revived;

    private void Start() => Initialize();

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        entityHealth = health;
        bodyHealth = health;
        if (entity != null)
        {
            entityRenderer = entity.GetComponent<SpriteRenderer>();
            entityOriginalSprite = entityRenderer != null ? entityRenderer.sprite : null;
        }
        ApplyStatusVisuals();
    }

    public void TakeDamage(int damage)
    {
        Initialize();
        if (!isActiveAndEnabled || damage <= 0 || entityHealth <= 0 || entityDefeated) return;
        entityHealth = Mathf.Max(0, entityHealth - damage);
        Debug.Log($"【敵人】實體受到 {damage} 點傷害，目前血量：{entityHealth}。", this);
        if (entityHealth != 0) return;

        entityDefeated = true;
        CombatInterrupted?.Invoke();
        reviveRoutine = StartCoroutine(ReviveEntity());
    }

    public bool TryReceivePlayerAttack(int damage, Vector3 attackPosition, float playerAttackRadius)
    {
        Initialize();
        if (!isActiveAndEnabled || damage <= 0 || IsFriendly || entityDefeated) return false;
        if (BodyCanBeHit && IsInsideHitRange(body.transform.position, bodyHitRadius, attackPosition, playerAttackRadius))
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
        SetActive(body, false);
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
        entityDefeated = false;
        CombatInterrupted?.Invoke();
        ApplyStatusVisuals();
        SetActive(body, false);
        BecameFriendly?.Invoke();
        Debug.Log("【敵人】本體血量歸零，已切換友好狀態並替換為 NPC Sprite。", this);
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

    private static void SetActive(GameObject targetObject, bool active)
    {
        if (targetObject != null && targetObject.activeSelf != active)
        {
            targetObject.SetActive(active);
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
