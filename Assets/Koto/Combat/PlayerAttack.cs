using UnityEngine;

/// <summary>
/// 玩家依指定方向物件攻擊。掃描模式啟用時不允許攻擊。
/// 將此元件掛在玩家物件上，並指定同一物件上的 ScanningSystem。
/// </summary>
public class PlayerAttack : MonoBehaviour
{
    [Header("攻擊輸入")]
    [Tooltip("按下此按鍵即可攻擊。")]
    [SerializeField] private KeyCode attackKey = KeyCode.F;
    [SerializeField] private ScanningSystem scanningSystem;

    [Header("攻擊中心")]
    [Tooltip("指定圓形攻擊範圍的中心位置。")]
    [SerializeField] private Transform attackDirection = null;

    [Header("攻擊判定")]
    [Tooltip("攻擊造成的傷害。")]
    [SerializeField, Min(1)] private int attackDamage = 20;
    [Tooltip("以攻擊方向物件為中心的圓形攻擊範圍半徑。")]
    [SerializeField, Min(0.01f)] private float attackRange = 2f;
    [SerializeField, Min(0f)] private float attackCooldown = 0.35f;

    private float nextAttackTime;

    private void Awake()
    {
        if (scanningSystem == null)
        {
            scanningSystem = GetComponent<ScanningSystem>();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(attackKey))
        {
            TryAttack();
        }
    }

    /// <summary>可由動畫事件或其他腳本呼叫的攻擊入口。</summary>
    public void TryAttack()
    {
        if (scanningSystem != null && scanningSystem.IsScanning)
        {
            Debug.Log("【攻擊系統】掃描模式中，無法攻擊。", this);
            return;
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        if (attackDirection == null)
        {
            Debug.LogWarning("【攻擊系統】請先在 Inspector 指定「攻擊方向」物件。", this);
            return;
        }

        nextAttackTime = Time.time + attackCooldown;
        Vector3 origin = attackDirection.position;
        EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        foreach (EnemyController enemy in enemies)
        {
            if (enemy == null || enemy.transform.root == transform.root)
            {
                continue;
            }

            enemy.TryReceivePlayerAttack(attackDamage, origin, attackRange);
        }

        Debug.Log("【攻擊系統】已使用圓形範圍發動攻擊。", this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (attackDirection == null)
        {
            return;
        }

        Vector3 origin = attackDirection.position;
        Gizmos.DrawWireSphere(origin, attackRange);
    }
}
