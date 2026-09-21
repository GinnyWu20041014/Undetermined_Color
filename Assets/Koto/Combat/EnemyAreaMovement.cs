using System;
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

    [InspectorName("移動區域")]
    [Tooltip("可增加多個 Collider。敵人不追蹤玩家時，會在這些區域內隨機移動。")]
    [SerializeField] private Collider[] movementAreas = System.Array.Empty<Collider>();

    private bool hasFriendlyMoveTarget;

    private Vector3 friendlyMoveTarget;
    private EnemyStateHealth state;
    private EnemyAnimationController animations;
    private EnemyPlayerAttackController combat;
    public Vector3 FrameDisplacement { get; private set; }
    public event Action<Vector3> Moved;

    private void OnEnable() => Bind();
    private void Start() => Bind();

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
        if (state != null) state.BecameFriendly -= ResetWanderTarget;
        FrameDisplacement = Vector3.zero;
        Moved?.Invoke(FrameDisplacement);
    }

    private void ResetWanderTarget() => hasFriendlyMoveTarget = false;

    private void Update()
    {
        FrameDisplacement = Vector3.zero;
        if (state != null && state.IsEntityDefeated) isKnockedBack = false;
        if (isKnockedBack)
        {
            float duration = Mathf.Max(0.01f, knockbackDuration);
            float before = Mathf.Clamp01(knockbackElapsed / duration);
            knockbackElapsed += Time.deltaTime;
            float after = Mathf.Clamp01(knockbackElapsed / duration);
            float distance = knockbackDistance * ((1f - before) * (1f - before) - (1f - after) * (1f - after));
            FrameDisplacement = knockbackDirection * distance;
            transform.position += FrameDisplacement;
            isKnockedBack = after < 1f;
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
            else if (combat.ChasePosition.HasValue)
                MoveOnXZ(combat.ChasePosition.Value);
        }
        Moved?.Invoke(FrameDisplacement);
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
        FrameDisplacement = actualDelta;
    }

    private float GetHorizontalDistance(Vector3 targetPosition)
    {
        Vector3 offset = targetPosition - transform.position;
        offset.y = 0f;
        return offset.magnitude;
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

        MoveOnXZ(friendlyMoveTarget);
    }

    private void SetFriendlyMoveTarget(Vector3 targetPosition)
    {
        friendlyMoveTarget = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
        hasFriendlyMoveTarget = true;
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
}
