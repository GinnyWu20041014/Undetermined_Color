using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Koto/Combat/Monster3 荊棘攻擊")]
public sealed class Monster3ThornAttack : MonoBehaviour
{
    [InspectorName("召喚物件（荊棘 Prefab）")]
    [SerializeField] private GameObject thornPrefab = null;
    [InspectorName("攻擊間隔（秒）")]
    [SerializeField, Min(0.1f)] private float attackInterval = 5f;
    [InspectorName("玩家附近生成半徑")]
    [SerializeField, Min(0.1f)] private float spawnRadius = 2f;
    [InspectorName("荊棘接觸半徑")]
    [SerializeField, Min(0.01f)] private float contactRadius = 0.45f;
    [InspectorName("減速後速度比例")]
    [SerializeField, Range(0.01f, 1f)] private float slowMultiplier = 0.5f;
    [InspectorName("警示動畫（選填）")]
    [Tooltip("未填時直接播放長出動畫，不產生警示圈。")]
    [SerializeField] private AnimationClip warningClip = null;
    [InspectorName("荊棘長出動畫")]
    [SerializeField] private AnimationClip growClip = null;
    private float nextAttackAt;
    public bool Ready => isActiveAndEnabled && thornPrefab != null && Time.time >= nextAttackAt;

    public void Release(PlayerHealth player)
    {
        if (!Ready || player == null || player.IsDead) return;
        nextAttackAt = Time.time + attackInterval;
        // 同一次施放的兩個點分處玩家兩側，避免完全重疊；位置不跟隨玩家。
        float angle = Random.Range(0f, Mathf.PI * 2f);
        for (int i = 0; i < 2; i++)
        {
            float direction = angle + i * Mathf.PI + Random.Range(-0.35f, 0.35f);
            float distance = Random.Range(spawnRadius * 0.4f, spawnRadius);
            Vector3 position = player.transform.position + new Vector3(Mathf.Cos(direction), 0f, Mathf.Sin(direction)) * distance;
            var anchor = new GameObject("Monster3 Thorn");
            anchor.transform.position = position;
            var animation = anchor.AddComponent<Monster3ThornAnimation>();
            animation.Play(thornPrefab, player, warningClip, growClip, contactRadius, slowMultiplier);
        }
    }
}
