using System.Collections;
using UnityEngine;

/// <summary>
/// 敵人的基礎控制器：處理友好／敵對狀態，以及敵對狀態下的玩家偵測與追蹤。
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
    [Tooltip("敵對狀態會偵測並追蹤玩家；友好狀態不會追蹤玩家。")]
    [SerializeField] private EnemyStatus status = EnemyStatus.敵對狀態;

    [InspectorName("血量")]
    [Tooltip("敵人的最大血量。")]
    [SerializeField, Min(1)] private int health = 100;

    [InspectorName("攻擊力")]
    [Tooltip("保留作為之後攻擊功能使用的傷害數值。")]
    [SerializeField, Min(0)] private int attackPower = 10;

    [InspectorName("實體復活時間")]
    [Tooltip("實體血量歸零後，在原地等待多久才復活。")]
    [SerializeField, Min(0f)] private float reviveDelay = 3f;

    /// <summary>提供後續攻擊功能讀取目前設定的攻擊力。</summary>
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
    [Tooltip("放入怪物的本體物件；僅在受到指定光源照射時顯示。")]
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

    [InspectorName("攻擊範圍")]
    [Tooltip("玩家進入此圓形範圍後，敵人會停止追擊。")]
    [SerializeField, Min(0.01f)] private float attackRange = 1.5f;

    [InspectorName("移動速度")]
    [Tooltip("敵人追蹤玩家時在 X/Z 平面上的移動速度。")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;

    [InspectorName("移動區域")]
    [Tooltip("可增加多個 Collider。友好狀態下，敵人只會在這些區域內隨機移動。")]
    [SerializeField] private Collider[] movementAreas = System.Array.Empty<Collider>();

    private int entityHealth;
    private int bodyHealth;
    private bool entityDefeated;
    private bool hasFriendlyMoveTarget;
    private Vector3 friendlyMoveTarget;
    private Vector3 revivePosition;
    private SpriteRenderer entityRenderer;
    private Sprite entityOriginalSprite;
    private Quaternion entityOriginalLocalRotation;

    private void Awake()
    {
        entityHealth = health;
        bodyHealth = health;
        if (entity != null)
        {
            entityRenderer = entity.GetComponent<SpriteRenderer>();
            entityOriginalSprite = entityRenderer != null ? entityRenderer.sprite : null;
            entityOriginalLocalRotation = entity.transform.localRotation;
        }

        SetActive(normalNpc, false);
        ApplyStatusVisuals();
        UpdateBodyVisibility();
    }

    private void Update()
    {
        UpdateBodyVisibility();

        if (entityDefeated)
        {
            return;
        }

        if (status == EnemyStatus.友好狀態)
        {
            WanderInsideAreas();
            return;
        }

        if (player == null)
        {
            return;
        }

        float playerDistance = GetHorizontalDistance(player.position);
        if (playerDistance > playerDetectionRange || playerDistance <= attackRange)
        {
            return;
        }

        MoveTowardsPlayer();
    }

    /// <summary>供其他腳本呼叫以扣除敵人血量。</summary>
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
            StartCoroutine(ReviveEntity());
        }
    }

    /// <summary>使用 X/Z 平面圓形距離判定玩家的攻擊是否命中實體或本體。</summary>
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
        // 目標與目前位置共用 Y，確保移動僅發生在 X、Z 軸且不旋轉敵人。
        Vector3 currentPosition = transform.position;
        Vector3 targetPosition = new Vector3(player.position.x, currentPosition.y, player.position.z);
        transform.position = Vector3.MoveTowards(
            currentPosition,
            targetPosition,
            moveSpeed * Time.deltaTime);
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
        revivePosition = transform.position;
        if (entity != null)
        {
            entity.transform.localRotation = entityOriginalLocalRotation * Quaternion.Euler(0f, 0f, -90f);
        }

        SetActive(body, false);
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
        Debug.Log("【敵人】實體已在原地復活，Z 軸角度已恢復並重新開始偵測玩家。", this);
    }

    private void BecomeFriendly()
    {
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

        Vector3 currentPosition = transform.position;
        Vector3 targetPosition = new Vector3(
            friendlyMoveTarget.x,
            currentPosition.y,
            friendlyMoveTarget.z);
        transform.position = Vector3.MoveTowards(
            currentPosition,
            targetPosition,
            moveSpeed * Time.deltaTime);
    }

    private void SetFriendlyMoveTarget(Vector3 targetPosition)
    {
        friendlyMoveTarget = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
        hasFriendlyMoveTarget = true;
    }

    private void UpdateBodyVisibility()
    {
        if (body != null)
        {
            SetActive(body, status == EnemyStatus.敵對狀態 && !entityDefeated && IsBodyIlluminated());
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
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
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
