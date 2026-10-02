using UnityEngine;

/// <summary>沿粒子雷射核心檢查 3D Collider，接觸玩家時強制死亡。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RedParticleRay))]
public class LaserDeathTrigger : MonoBehaviour
{
    [InspectorName("玩家碰撞圖層")]
    [Tooltip("只檢查這些圖層的 Collider；必須包含玩家 Collider 所在圖層。")]
    [SerializeField] private LayerMask playerLayers = ~0;
    [InspectorName("額外碰觸半徑")]
    [Tooltip("加在 Width / 2 上的世界單位半徑；0 使用雷射核心寬度。")]
    [SerializeField, Min(0f)] private float extraHitRadius = 0f;

    private RedParticleRay ray;
    private Collider[] hits = new Collider[16];

    private void Awake() { ray = GetComponent<RedParticleRay>(); }

    private void FixedUpdate()
    {
        if (ray == null || !ray.isActiveAndEnabled || ray.brightness <= 0f || ray.rayColor.a <= 0f) return;
        float length = Mathf.Clamp(ray.length, 0f, Mathf.Max(0f, ray.maxLength));
        if (length <= 0f) return;
        Vector3 start = transform.position;
        Vector3 end = start + transform.right * length;
        float radius = Mathf.Max(0.001f, ray.width) * 0.5f + Mathf.Max(0f, extraHitRadius);

        int count;
        // Grow if full so scenery colliders cannot hide a player's contact.
        while (true)
        {
            count = Physics.OverlapCapsuleNonAlloc(start, end, radius, hits, playerLayers, QueryTriggerInteraction.Collide);
            if (count < hits.Length) break;
            System.Array.Resize(ref hits, hits.Length * 2);
        }
        for (int i = 0; i < count; i++)
        {
            PlayerHealth health = hits[i].GetComponentInParent<PlayerHealth>();
            if (health == null && hits[i].attachedRigidbody != null)
                health = hits[i].attachedRigidbody.GetComponentInChildren<PlayerHealth>();
            if (health != null && health.isActiveAndEnabled && !health.IsDead)
                health.ForceDeath();
            hits[i] = null;
        }
    }
}
