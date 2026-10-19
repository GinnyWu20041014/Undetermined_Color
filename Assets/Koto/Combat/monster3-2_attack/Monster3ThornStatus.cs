using UnityEngine;

/// <summary>荊棘接觸效果：每秒扣 1 血，共 5 秒；重複接觸刷新時間，不疊加傷害。</summary>
[DisallowMultipleComponent]
public sealed class Monster3ThornStatus : MonoBehaviour
{
    private PlayerHealth health;
    private PlayerMovement movement;
    private float expiresAt;
    private float nextTick;
    private bool active;

    public void Apply(PlayerHealth target, float speedMultiplier)
    {
        health = target;
        if (movement == null) movement = target.GetComponentInParent<PlayerMovement>();
        if (movement == null) movement = target.GetComponentInChildren<PlayerMovement>();
        if (!active) nextTick = Time.time + 1f;
        active = true;
        expiresAt = Time.time + 5f;
        if (movement != null) movement.ThornSpeedMultiplier = Mathf.Clamp01(speedMultiplier);
    }

    private void Update()
    {
        if (!active) return;
        if (health == null || health.IsDead) { Clear(); return; }
        while (nextTick <= Time.time && nextTick <= expiresAt)
        {
            nextTick += 1f;
            health.ReceiveDamageOverTime(1);
            if (health.IsDead) { Clear(); return; }
        }
        if (Time.time >= expiresAt) Clear();
    }

    private void Clear()
    {
        active = false;
        if (movement != null) movement.ThornSpeedMultiplier = 1f;
    }

    private void OnDisable() => Clear();
}
