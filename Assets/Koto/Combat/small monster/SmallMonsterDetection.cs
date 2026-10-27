using UnityEngine;

[DisallowMultipleComponent]
public sealed class SmallMonsterDetection : MonoBehaviour
{
    [SerializeField, InspectorName("玩家（留空自動尋找 Player Tag）")] private Transform player;
    [SerializeField, Min(0.1f), InspectorName("玩家偵測半徑")] private float detectionRadius = 8f;
    public PlayerHealth Target { get; private set; }
    private float nextSearch;

    private void Update()
    {
        if (player == null && Time.time >= nextSearch)
        {
            nextSearch = Time.time + 0.5f;
            foreach (var candidate in GameObject.FindGameObjectsWithTag("Player"))
                if (candidate.scene == gameObject.scene) { player = candidate.transform; break; }
        }
        Target = null;
        if (player == null || !player.gameObject.activeInHierarchy) return;
        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        var health = player.GetComponentInParent<PlayerHealth>();
        if (health != null && health.isActiveAndEnabled && !health.IsDead && offset.sqrMagnitude <= detectionRadius * detectionRadius)
            Target = health;
    }
    private void OnDisable() => Target = null;
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
