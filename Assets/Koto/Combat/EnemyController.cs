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
    [Tooltip("實體與本體共用的生命值。")]
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
    [InspectorName("移動速度")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;
    [InspectorName("移動範圍")]
    [Tooltip("實體未偵測到玩家時，會在此 Collider 範圍內隨機移動。")]
    [SerializeField] private Collider movementArea = null;

    private const float ReviveDelaySeconds = 3f;

    private int currentHealth;
    private bool isEntityDefeated;
    private Vector3 bodyLocalPosition;
    private Quaternion bodyLocalRotation;
    private Vector3 wanderTarget;

    /// <summary>供其他敵人攻擊邏輯讀取的攻擊力。</summary>
    public int AttackPower => attackPower;

    private void Awake()
    {
        currentHealth = health;
        status = EnemyStatus.敵對狀態;
        wanderTarget = entity != null ? entity.transform.position : transform.position;

        if (body != null && entity != null)
        {
            bodyLocalPosition = entity.transform.InverseTransformPoint(body.transform.position);
            bodyLocalRotation = Quaternion.Inverse(entity.transform.rotation) * body.transform.rotation;
        }

        SetActive(body, false);
        SetActive(normalNpc, false);
    }

    private void Update()
    {
        if (entity == null)
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
            WanderInsideMovementArea();
            return;
        }

        if (IsPlayerDetected())
        {
            MoveEntity(player.position);
        }
        else
        {
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
        if (damage <= 0 || isEntityDefeated || status == EnemyStatus.友好狀態 || hitCollider == null)
        {
            return;
        }

        if (IsPartOf(hitCollider, body))
        {
            currentHealth = Mathf.Max(0, currentHealth - damage);
            if (currentHealth == 0)
            {
                BecomeFriendlyNpc();
            }

            return;
        }

        if (!IsPartOf(hitCollider, entity))
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        if (currentHealth == 0)
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

        Vector3 entityPosition = Flatten(entity.transform.position);
        Vector3 playerPosition = Flatten(player.position);
        return Vector3.Distance(entityPosition, playerPosition) <= playerDetectionRange;
    }

    private void WanderInsideMovementArea()
    {
        if (movementArea == null)
        {
            return;
        }

        Vector3 entityPosition = Flatten(entity.transform.position);
        if (!IsInsideMovementArea(entityPosition))
        {
            MoveEntity(movementArea.ClosestPoint(entity.transform.position));
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
                entity.transform.position.y,
                Random.Range(bounds.min.z, bounds.max.z));

            if (IsInsideMovementArea(candidate))
            {
                return candidate;
            }
        }

        Vector3 center = bounds.center;
        return new Vector3(center.x, entity.transform.position.y, center.z);
    }

    private void MoveEntity(Vector3 destination)
    {
        Vector3 currentPosition = entity.transform.position;
        Vector3 targetPosition = new Vector3(destination.x, currentPosition.y, destination.z);
        // 只移動 X、Z 軸，且不改變實體原本的旋轉。
        entity.transform.position = Vector3.MoveTowards(currentPosition, targetPosition, moveSpeed * Time.deltaTime);
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
        SetActive(entity, false);
        SetActive(body, false);
        yield return new WaitForSeconds(ReviveDelaySeconds);

        currentHealth = health;
        isEntityDefeated = false;
        SetActive(entity, true);
    }

    private void BecomeFriendlyNpc()
    {
        status = EnemyStatus.友好狀態;
        StopAllCoroutines();
        SetActive(entity, false);
        SetActive(body, false);

        // 將目前顯示與移動的實體替換為一般 NPC。
        entity = normalNpc;
        SetActive(entity, true);

        if (entity != null)
        {
            wanderTarget = entity.transform.position;
        }
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
