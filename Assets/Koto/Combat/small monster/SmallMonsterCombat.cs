using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SmallMonsterDetection), typeof(SmallMonsterAnimation), typeof(EnemyAreaMovement))]
public sealed class SmallMonsterCombat : MonoBehaviour
{
    [SerializeField, Min(1), InspectorName("生命值")] private int maxHealth = 10;
    [SerializeField, Min(0.01f), InspectorName("受擊半徑")] private float hitRadius = 0.65f;
    [Tooltip("分裂後的子體攻擊力為母體的一半，小數向下取整。")]
    [SerializeField, Min(0), InspectorName("攻擊力")] private int attackDamage = 5;
    [SerializeField, InspectorName("攻擊點")] private Transform attackPoint;
    [SerializeField, Min(0.01f), InspectorName("攻擊點半徑")] private float attackRadius = 0.65f;
    [SerializeField, Min(0.01f), InspectorName("攻擊間隔（秒）")] private float attackInterval = 1.5f;
    [SerializeField, Range(0f, 1f), InspectorName("命中動畫進度")] private float hitProgress = 0.35f;
    [SerializeField, Min(0.05f), InspectorName("分裂個體間距")]
    [Tooltip("分裂時個體受擊範圍之間額外保留的距離。")]
    private float splitGap = 0.25f;
    [SerializeField, HideInInspector] private int generation;
    private int health;
    private bool dead, summoning, attacking;
    private float nextAttack;
    private SmallMonsterDetection detection;
    private SmallMonsterAnimation animations;
    private EnemyAreaMovement movement;
    public int CurrentHealth => health;
    public int MaxHealth => Mathf.Max(1, maxHealth);
    public int AttackDamage => attackDamage;
    public int Generation => generation;
    public bool IsDead => dead;
    public bool CanMove => isActiveAndEnabled && !dead && !summoning && !attacking;
    public Vector3? ChasePosition { get; private set; }
    public bool WantsToWander => detection == null || detection.Target == null;
    private float Size => Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
    private Vector3 AttackPosition => attackPoint != null ? attackPoint.position : transform.position;

    private void Awake()
    {
        health = MaxHealth;
        dead = summoning = attacking = false;
        nextAttack = 0f;
        detection = GetComponent<SmallMonsterDetection>();
        animations = GetComponent<SmallMonsterAnimation>();
        movement = GetComponent<EnemyAreaMovement>();
    }
    private IEnumerator Start()
    {
        summoning = true;
        animations.PlaySummon();
        yield return new WaitForSeconds(animations.SummonDuration);
        summoning = false;
    }
    private void Update()
    {
        ChasePosition = null;
        if (!CanMove || movement.IsKnockedBack) return;
        var target = detection.Target;
        if (target == null) return;
        Vector3 offset = target.transform.position - AttackPosition;
        offset.y = 0f;
        float gap = offset.magnitude - (attackRadius * Size + target.HitRadius) * 0.85f;
        if (gap > 0f) ChasePosition = transform.position + offset.normalized * gap;
        else if (Time.time >= nextAttack) StartCoroutine(Attack(target));
    }
    private void LateUpdate()
    {
        if (!dead && !summoning && !attacking)
            animations.Locomotion(movement.FrameDisplacement.sqrMagnitude > 0.000001f);
    }
    private IEnumerator Attack(PlayerHealth target)
    {
        attacking = true;
        nextAttack = Time.time + Mathf.Max(0.01f, attackInterval);
        animations.PlayAttack();
        float duration = animations.AttackDuration;
        yield return new WaitForSeconds(duration * hitProgress);
        if (!dead && !movement.IsKnockedBack && target != null && target.isActiveAndEnabled)
            target.TryReceiveEnemyAttack(attackDamage, AttackPosition, attackRadius * Size, transform.position);
        yield return new WaitForSeconds(duration * (1f - hitProgress));
        attacking = false;
    }
    public bool TryReceivePlayerAttack(int damage, Vector3 position, float radius, Vector3 attackerPosition)
    {
        if (!isActiveAndEnabled || dead || summoning || damage <= 0) return false;
        Vector3 offset = transform.position - position;
        offset.y = 0f;
        if (offset.sqrMagnitude > Mathf.Pow(radius + hitRadius * Size, 2f)) return false;
        AttackFeedbackController.NotifyPlayerHit(gameObject, gameObject, Mathf.Min(health, damage));
        health = Mathf.Max(0, health - damage);
        if (health == 0)
        {
            dead = true;
            StopAllCoroutines();
            attacking = summoning = false;
            movement.enabled = false;
            StartCoroutine(Die());
        }
        else
        {
            StopAllCoroutines();
            attacking = false;
            movement.ApplyKnockback(attackerPosition);
        }
        return true;
    }
    private IEnumerator Die()
    {
        animations.PlayDeath();
        yield return new WaitForSeconds(animations.DeathDuration);
        if (generation < 2)
        {
            // 先停用再複製，確保設定世代與尺寸後才執行子體 Awake／Start。
            var holder = new GameObject("分裂初始化");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
            holder.SetActive(false);
            float childScale = generation == 0 ? 0.8f : 0.5f;
            float childRadius = hitRadius * childScale;
            var neighbours = FindObjectsByType<SmallMonsterCombat>(FindObjectsSortMode.None);
            var positions = new List<Vector3>(2);
            for (int i = 0; i < 2; i++)
            {
                var child = Instantiate(gameObject, holder.transform);
                child.name = "分裂小球";
                var combat = child.GetComponent<SmallMonsterCombat>();
                combat.generation = generation + 1;
                // 子體擁有獨立生命狀態，不能沿用母體的死亡狀態與剩餘血量。
                combat.health = combat.MaxHealth;
                combat.dead = combat.summoning = combat.attacking = false;
                combat.nextAttack = 0f;
                combat.attackDamage = Mathf.Max(0, attackDamage) / 2;
                Vector3 position = FindSplitPosition(childRadius, i, neighbours, positions);
                positions.Add(position);
                child.transform.SetPositionAndRotation(position, transform.rotation);
                child.transform.localScale = Vector3.one * childScale;
                child.GetComponent<EnemyAreaMovement>().enabled = true;
                child.transform.SetParent(null, true);
            }
            Destroy(holder);
        }
        Destroy(gameObject);
    }

    private Vector3 FindSplitPosition(float radius, int childIndex, SmallMonsterCombat[] neighbours, List<Vector3> reserved)
    {
        Vector3 direction = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.right;
        float gap = Mathf.Max(0.05f, splitGap);
        float initialDistance = radius + gap * 0.5f;
        // 對稱向兩側生成；附近已有分裂體時，沿圓周及外圈找空位。
        for (int ring = 1; ; ring++)
        {
            for (int step = 0; step < 16; step++)
            {
                float angle = childIndex * 180f + step * 22.5f;
                Vector3 position = transform.position + Quaternion.AngleAxis(angle, Vector3.up) * direction * (initialDistance * ring);
                bool clear = true;
                foreach (var other in neighbours)
                {
                    if (other == null || other == this || other.dead || !other.isActiveAndEnabled || other.gameObject.scene != gameObject.scene) continue;
                    float distance = radius + other.hitRadius * other.Size + gap;
                    if (Vector3.ProjectOnPlane(position - other.transform.position, Vector3.up).sqrMagnitude < distance * distance)
                    { clear = false; break; }
                }
                if (!clear) continue;
                foreach (var other in reserved)
                {
                    float distance = radius * 2f + gap;
                    if (Vector3.ProjectOnPlane(position - other, Vector3.up).sqrMagnitude + 0.00001f < distance * distance)
                    { clear = false; break; }
                }
                if (clear) return position;
            }
        }
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(AttackPosition, attackRadius * Size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hitRadius * Size);
    }
}
