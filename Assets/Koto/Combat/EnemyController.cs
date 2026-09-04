using UnityEngine;

/// <summary>
/// 敵人的基礎數值與受傷處理。生命值不會建立玩家可見的 UI。
/// 將此元件與 Collider 掛在敵人物件（或其父物件）上。
/// </summary>
public class EnemyController : MonoBehaviour
{
    [Header("敵人數值")]
    [Tooltip("敵人的生命值；只供遊戲內部計算，不會顯示給玩家。")]
    [SerializeField, Min(1)] private int health = 100;
    [Tooltip("敵人的攻擊力，供敵人攻擊玩家的功能讀取。")]
    [SerializeField, Min(0)] private int attackPower = 10;

    private int currentHealth;

    /// <summary>供敵方攻擊邏輯讀取的攻擊力。</summary>
    public int AttackPower => attackPower;

    private void Awake()
    {
        currentHealth = health;
    }

    /// <summary>承受傷害；生命值歸零時移除敵人物件。</summary>
    public void TakeDamage(int damage)
    {
        if (damage <= 0 || currentHealth <= 0)
        {
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        Debug.Log($"【敵人】{name} 受到 {damage} 點傷害。", this);

        if (currentHealth == 0)
        {
            Debug.Log($"【敵人】{name} 已被擊敗。", this);
            Destroy(gameObject);
        }
    }
}
