using System.Collections.Generic;
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

    [Header("攻擊方向")]
    [Tooltip("指定攻擊的起點與方向。攻擊會由此物件的位置，沿它的藍色 Z 軸方向發出。")]
    [SerializeField] private Transform attackDirection = null;

    [Header("攻擊判定")]
    [Tooltip("攻擊造成的傷害。")]
    [SerializeField, Min(1)] private int attackDamage = 20;
    [Tooltip("從攻擊方向物件偵測敵人的距離。")]
    [SerializeField, Min(0.01f)] private float attackRange = 2f;
    [Tooltip("攻擊判定的寬度。")]
    [SerializeField, Min(0.01f)] private float attackRadius = 0.5f;
    [Tooltip("可被攻擊的圖層。")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [SerializeField, Min(0f)] private float attackCooldown = 0.35f;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    private readonly RaycastHit[] attackHits = new RaycastHit[16];
    private readonly HashSet<EnemyController> hitEnemies = new HashSet<EnemyController>();
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
        hitEnemies.Clear();

        Vector3 origin = attackDirection.position;
        Vector3 direction = attackDirection.forward;
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            attackRadius,
            direction,
            attackHits,
            attackRange,
            targetLayers,
            triggerInteraction);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = attackHits[i].collider;
            if (hitCollider == null || hitCollider.transform.root == transform.root)
            {
                continue;
            }

            EnemyController enemy = hitCollider.GetComponentInParent<EnemyController>();
            if (enemy != null && hitEnemies.Add(enemy))
            {
                enemy.TakeDamage(attackDamage);
            }
        }

        Debug.Log("【攻擊系統】已依指定攻擊方向發動攻擊。", this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (attackDirection == null)
        {
            return;
        }

        Vector3 origin = attackDirection.position;
        Vector3 direction = attackDirection.forward;
        Gizmos.DrawWireSphere(origin, attackRadius);
        Gizmos.DrawWireSphere(origin + direction * attackRange, attackRadius);
        Gizmos.DrawLine(origin, origin + direction * attackRange);
    }
}
