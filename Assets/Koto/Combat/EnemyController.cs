using System.Collections;
using UnityEngine;

/// <summary>
/// 管理敵人的實體、本體與一般 NPC 狀態。
/// 此腳本應掛在獨立的父物件，並將三種物件分別指定到 Inspector 欄位。
/// </summary>
public class EnemyController : MonoBehaviour
{
    private enum EnemyStatus
    {
        敵對狀態,
        友好狀態
    }

    [Header("敵人數值")]
    [InspectorName("生命值")]
    [Tooltip("實體與本體各自使用此數值作為初始生命值。")]
    [SerializeField, Min(1)] private int health = 100;
    [InspectorName("攻擊力")]
    [Tooltip("敵人的攻擊力數值。")]
    [SerializeField, Min(0)] private int attackPower = 10;
    [InspectorName("顯示狀態")]
    [Tooltip("本體未被擊殺時為敵對狀態；本體被擊殺後自動切換為友好狀態。")]
    [SerializeField] private EnemyStatus status = EnemyStatus.敵對狀態;

    [Header("敵人物件")]
    [InspectorName("實體")]
    [Tooltip("會移動、可被攻擊後暫時消失並復活的敵人物件。")]
    [SerializeField] private GameObject entity = null;
    [InspectorName("本體")]
    [Tooltip("只有受到光源照射時才會出現；擊殺後敵人會恢復成一般 NPC。")]
    [SerializeField] private GameObject body = null;
    [InspectorName("一般 NPC")]
    [Tooltip("本體被擊殺後顯示的一般 NPC 物件。")]
    [SerializeField] private GameObject normalNpc = null;

    [Header("Enemy1 圖像顯示")]
    [InspectorName("敵對圖像")]
    [Tooltip("Enemy1 在敵對狀態時顯示的 Entity Sprite。")]
    [SerializeField] private Sprite entitySprite = null;
    [InspectorName("友好圖像")]
    [Tooltip("Enemy1 在友好狀態時顯示的 NPC Sprite。")]
    [SerializeField] private Sprite npcSprite = null;

    [Header("偵測目標")]
    [InspectorName("偵測光源")]
    [Tooltip("可加入多個光源；本體在任一光源照射範圍內時會顯示。")]
    [SerializeField] private Light[] lightSources = System.Array.Empty<Light>();
    [InspectorName("偵測玩家")]
    [Tooltip("敵人偵測與追蹤的玩家。")]
    [SerializeField] private Transform player = null;

    [Header("移動設定")]
    [InspectorName("玩家偵測範圍")]
    [Tooltip("玩家進入此圓形範圍時，實體會離開巡邏範圍並追蹤玩家。")]
    [SerializeField, Min(0.01f)] private float playerDetectionRange = 8f;
    [InspectorName("攻擊範圍")]
    [Tooltip("玩家進入此圓形範圍時，實體停止追擊並對玩家攻擊；離開後會恢復追擊。")]
    [SerializeField, Min(0.01f)] private float attackRange = 1.5f;
    [InspectorName("移動速度")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;
    [InspectorName("移動範圍")]
    [Tooltip("實體未偵測到玩家時，會在此 Collider 範圍內隨機移動。")]
    [SerializeField] private Collider movementArea = null;

    private const float ReviveDelaySeconds = 3f;

    private int entityHealth;
    private int bodyHealth;
    private bool isEntityDefeated;
    private Vector3 bodyLocalPosition;
    private Quaternion bodyLocalRotation;
    private Quaternion entityOriginalLocalRotation;
    private Vector3 wanderTarget;
    private bool hasAttackedPlayerInRange;
    private SpriteRenderer entitySpriteRenderer;

    /// <summary>供其他敵人攻擊邏輯讀取的攻擊力。</summary>
    public int AttackPower => attackPower;

    private void Awake()
    {
        entityHealth = health;
        bodyHealth = health;
        status = EnemyStatus.敵對狀態;
        wanderTarget = entity != null ? entity.transform.position : transform.position;

        if (entity != null)
        {
            entityOriginalLocalRotation = entity.transform.localRotation;
            entitySpriteRenderer = entity.GetComponent<SpriteRenderer>();
        }

        if (body != null && entity != null)
        {
            bodyLocalPosition = entity.transform.InverseTransformPoint(body.transform.position);
            bodyLocalRotation = Quaternion.Inverse(entity.transform.rotation) * body.transform.rotation;
        }

        SetActive(body, false);
        SetActive(normalNpc, false);
        UpdateEntitySprite();
    }

    private void Update()
    {
        if (GetMovingObject() == null)
        {
            return;
        }

        SetActive(body, status == EnemyStatus.敵對狀態 && !isEntityDefeated && IsIlluminated());

        if (isEntityDefeated)
        {
            return;
        }

        if (status == EnemyStatus.友好狀態)
        {
            UpdateEntitySprite();
            WanderInsideMovementArea();
            return;
        }

        if (IsPlayerDetected())
        {
            UpdatePlayerChaseAndAttack();
        }
        else
        {
            hasAttackedPlayerInRange = false;
            WanderInsideMovementArea();
        }
    }

    private void LateUpdate()
    {
        if (body == null || entity == null || body == entity)
        {
            return;
        }

        // 本體沒有自己的移動，固定跟隨實體的位置與旋轉。
        body.transform.SetPositionAndRotation(
            entity.transform.TransformPoint(bodyLocalPosition),
            entity.transform.rotation * bodyLocalRotation);
    }

    /// <summary>由玩家攻擊腳本呼叫，並依命中的實體或本體處理結果。</summary>
    public void TakeDamage(int damage, Collider hitCollider)
    {
        if (damage <= 0 || status == EnemyStatus.友好狀態 || hitCollider == null)
        {
            return;
        }

        if (IsPartOf(hitCollider, body))
        {
            bodyHealth = Mathf.Max(0, bodyHealth - damage);
            Debug.Log($"【敵人】本體受到 {damage} 點傷害，目前生命值：{bodyHealth}。", this);
            if (bodyHealth == 0)
            {
                BecomeFriendlyNpc();
            }

            return;
        }

        if (!IsPartOf(hitCollider, entity) || isEntityDefeated)
        {
            return;
        }

        entityHealth = Mathf.Max(0, entityHealth - damage);
        Debug.Log($"【敵人】實體受到 {damage} 點傷害，目前生命值：{entityHealth}。", this);
        if (entityHealth == 0)
        {
            StartCoroutine(ReviveEntity());
        }
    }

    /// <summary>保留給未提供命中 Collider 的外部呼叫。</summary>
    public void TakeDamage(int damage)
    {
        TakeDamage(damage, null);
    }

    private bool IsPlayerDetected()
    {
        if (player == null)
        {
            return false;
        }

        Vector3 entityPosition = Flatten(GetMovingObject().transform.position);
        Vector3 playerPosition = Flatten(player.position);
        return Vector3.Distance(entityPosition, playerPosition) <= playerDetectionRange;
    }

    private void UpdatePlayerChaseAndAttack()
    {
        float distanceToPlayer = Vector3.Distance(
            Flatten(GetMovingObject().transform.position),
            Flatten(player.position));

        if (distanceToPlayer > attackRange)
        {
            hasAttackedPlayerInRange = false;
            MoveEntity(player.position);
            return;
        }

        // 進入攻擊範圍時停止追擊並攻擊一次；離開範圍後才可再次攻擊。
        if (!hasAttackedPlayerInRange)
        {
            hasAttackedPlayerInRange = true;
            player.SendMessage("TakeDamage", attackPower, SendMessageOptions.DontRequireReceiver);
        }
    }

    private void WanderInsideMovementArea()
    {
        if (movementArea == null)
        {
            return;
        }

        GameObject movingObject = GetMovingObject();
        Vector3 entityPosition = Flatten(movingObject.transform.position);
        if (!IsInsideMovementArea(entityPosition))
        {
            MoveEntity(movementArea.ClosestPoint(movingObject.transform.position));
            return;
        }

        if (Vector3.Distance(entityPosition, Flatten(wanderTarget)) <= 0.05f)
        {
            wanderTarget = GetRandomPointInMovementArea();
        }

        MoveEntity(wanderTarget);
    }

    private bool IsInsideMovementArea(Vector3 position)
    {
        return (movementArea.ClosestPoint(position) - position).sqrMagnitude < 0.0001f;
    }

    private Vector3 GetRandomPointInMovementArea()
    {
        Bounds bounds = movementArea.bounds;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                GetMovingObject().transform.position.y,
                Random.Range(bounds.min.z, bounds.max.z));

            if (IsInsideMovementArea(candidate))
            {
                return candidate;
            }
        }

        Vector3 center = bounds.center;
        return new Vector3(center.x, GetMovingObject().transform.position.y, center.z);
    }

    private void MoveEntity(Vector3 destination)
    {
        GameObject movingObject = GetMovingObject();
        Vector3 currentPosition = movingObject.transform.position;
        Vector3 targetPosition = new Vector3(destination.x, currentPosition.y, destination.z);
        // 只移動 X、Z 軸，且不改變實體原本的旋轉。
        movingObject.transform.position = Vector3.MoveTowards(currentPosition, targetPosition, moveSpeed * Time.deltaTime);
    }

    private bool IsIlluminated()
    {
        foreach (Light lightSource in lightSources)
        {
            if (IsWithinLight(lightSource))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsWithinLight(Light lightSource)
    {
        if (lightSource == null || !lightSource.isActiveAndEnabled || entity == null)
        {
            return false;
        }

        if (lightSource.type == LightType.Directional)
        {
            return true;
        }

        Vector3 offset = entity.transform.position - lightSource.transform.position;
        if (offset.sqrMagnitude > lightSource.range * lightSource.range)
        {
            return false;
        }

        return lightSource.type != LightType.Spot ||
               Vector3.Angle(lightSource.transform.forward, offset) <= lightSource.spotAngle * 0.5f;
    }

    private IEnumerator ReviveEntity()
    {
        isEntityDefeated = true;
        entity.transform.localRotation = entityOriginalLocalRotation * Quaternion.Euler(90f, 0f, 0f);
        SetActive(body, false);
        Debug.Log($"【敵人】實體生命值歸零，已倒下；{ReviveDelaySeconds:0.##} 秒後復活。", this);
        yield return new WaitForSeconds(ReviveDelaySeconds);

        entityHealth = health;
        isEntityDefeated = false;
        entity.transform.localRotation = entityOriginalLocalRotation;
        Debug.Log("【敵人】實體已復活，生命值已恢復。", this);
    }

    private void BecomeFriendlyNpc()
    {
        status = EnemyStatus.友好狀態;
        StopAllCoroutines();
        SetActive(body, false);

        // Entity 物件保持啟用以保留移動；僅替換 Enemy1 的 Sprite 為 NPC。
        UpdateEntitySprite();
        wanderTarget = entity.transform.position;

        Debug.Log("【敵人】本體生命值歸零，已切換為友好狀態；Enemy1 已改顯示 NPC 圖像。", this);
    }

    private GameObject GetMovingObject()
    {
        return entity;
    }

    private void UpdateEntitySprite()
    {
        if (entitySpriteRenderer == null)
        {
            return;
        }

        entitySpriteRenderer.sprite = status == EnemyStatus.敵對狀態 ? entitySprite : npcSprite;
    }

    private static bool IsPartOf(Collider hitCollider, GameObject targetObject)
    {
        return targetObject != null &&
               (hitCollider.transform == targetObject.transform || hitCollider.transform.IsChildOf(targetObject.transform));
    }

    private static Vector3 Flatten(Vector3 position)
    {
        return new Vector3(position.x, 0f, position.z);
    }

    private static void SetActive(GameObject targetObject, bool active)
    {
        if (targetObject != null && targetObject.activeSelf != active)
        {
            targetObject.SetActive(active);
        }
    }
}
