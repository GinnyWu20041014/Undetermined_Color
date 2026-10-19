using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>自行執行區域遊蕩與 X/Z 位移；讀取攻擊意圖，以位移事件通知動畫元件。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Combat/敵人區域移動")]
public sealed class EnemyAreaMovement : MonoBehaviour
{
    [InspectorName("移動速度")]
    [Tooltip("敵人追蹤玩家或遊蕩時在 X/Z 平面上的移動速度。")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;

    [Header("受擊擊退")]
    [SerializeField, Min(0f)] private float knockbackDistance = 0.8f;
    [SerializeField, Min(0.01f)] private float knockbackDuration = 0.2f;
    private Vector3 knockbackDirection;
    private float knockbackElapsed;
    private bool isKnockedBack;

    public void ApplyKnockback(Vector3 attackerPosition)
    {
        knockbackDirection = Vector3.ProjectOnPlane(transform.position - attackerPosition, Vector3.up).normalized;
        if (knockbackDirection.sqrMagnitude < 0.0001f) knockbackDirection = Vector3.back;
        knockbackElapsed = 0f;
        isKnockedBack = knockbackDistance > 0f;
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
    private EnemyStateHealth state;
    private EnemyAnimationController animations;
    private EnemyPlayerAttackController combat;
    public Vector3 FrameDisplacement { get; private set; }
    public event Action<Vector3> Moved;

    private void OnEnable() => Bind();
    private void Start()
    {
        Bind();
        avoidanceSide = Random.value < 0.5f ? -1f : 1f;
    }

    private void Bind()
    {
        if (state != null) state.BecameFriendly -= ResetWanderTarget;
        state = GetComponent<EnemyStateHealth>();
        animations = GetComponent<EnemyAnimationController>();
        combat = GetComponent<EnemyPlayerAttackController>();
        if (state != null) state.BecameFriendly += ResetWanderTarget;
    }

    private void OnDisable()
    {
        isKnockedBack = false;
        ResetWanderTarget();
        if (state != null) state.BecameFriendly -= ResetWanderTarget;
        FrameDisplacement = Vector3.zero;
        Moved?.Invoke(FrameDisplacement);
    }

    private void ResetWanderTarget()
    {
        hasFriendlyMoveTarget = false;
        wanderIdleUntil = 0f;
    }

    private void Update()
    {
        RefreshFloors();
        FrameDisplacement = Vector3.zero;
        if (state != null && state.IsEntityDefeated) isKnockedBack = false;
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
            else knockbackElapsed = duration;
            isKnockedBack = knockbackElapsed < duration;
            Moved?.Invoke(FrameDisplacement);
            return;
        }
        if ((state == null || (state.isActiveAndEnabled && !state.IsEntityDefeated)) &&
            (animations == null || !animations.IsSummoning))
        {
            if (state != null && state.IsFriendly)
                WanderInsideAreas();
            else if (combat == null || !combat.isActiveAndEnabled || combat.WantsToWander)
                WanderInsideAreas();
            else
            {
                // 進入追擊或攻擊時立即取消遊蕩待機。
                ResetWanderTarget();
                if (combat.ChasePosition.HasValue)
                    MoveOnXZ(combat.ChasePosition.Value);
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
