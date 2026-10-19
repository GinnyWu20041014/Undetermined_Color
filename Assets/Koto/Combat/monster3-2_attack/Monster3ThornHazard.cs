using UnityEngine;

/// <summary>沿用玩家 X/Z 受擊圓判斷接觸；不要求玩家具備特定 Collider 或 Rigidbody。</summary>
public sealed class Monster3ThornHazard : MonoBehaviour
{
    private PlayerHealth player;
    private float radius;
    private float slow;
    private float expiresAt;
    private bool armed;

    public void Arm(PlayerHealth target, float contactRadius, float speedMultiplier)
    {
        player = target;
        radius = contactRadius;
        slow = speedMultiplier;
        expiresAt = Time.time + 5f;
        armed = true;
    }

    private void Update()
    {
        if (!armed) return;
        if (Time.time >= expiresAt) { Destroy(gameObject); return; }
        if (player == null || player.IsDead || !player.IsInsideEnemyAttackRange(transform.position, radius)) return;
        armed = false;
        var status = player.GetComponent<Monster3ThornStatus>();
        if (status == null) status = player.gameObject.AddComponent<Monster3ThornStatus>();
        status.enabled = true;
        status.Apply(player, slow);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
