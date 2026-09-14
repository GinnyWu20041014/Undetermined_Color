using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 舊場景資料移轉用。只保留舊欄位與相容入口，不執行移動、血量、動畫或攻擊迴圈。
/// 新敵人請直接掛載四個獨立元件；舊敵人可在 Inspector 一鍵移轉後移除此元件。
/// </summary>
[DefaultExecutionOrder(-1000)]
[AddComponentMenu("")]
public sealed class EnemyController : MonoBehaviour
{
    public enum EnemyStatus
    {
        敵對狀態,
        友好狀態
    }
#pragma warning disable CS0414

    [HideInInspector]
    [Header("玩家偵測與移動")]
    [InspectorName("偵測玩家")]
    [Tooltip("要被敵人偵測的玩家 Transform。")]
    [SerializeField] private Transform player = null;

    [HideInInspector]
    [InspectorName("玩家偵測半徑")]
    [Tooltip("以敵人為中心，在 X/Z 平面向外擴散的圓形偵測範圍。")]
    [SerializeField, Min(0.01f)] private float playerDetectionRange = 8f;

    [HideInInspector]
    [InspectorName("停止追擊距離")]
    [Tooltip("只決定何時停止移動並開始揮擊；真正是否造成傷害完全由攻擊骨架接觸玩家判定。")]
    [SerializeField, Min(0.01f), FormerlySerializedAs("attackRange")] private float stopChasingDistance = 1.5f;

    [HideInInspector]
    [InspectorName("實體攻擊骨架")]
    [Tooltip("可增加多個手部、爪子或武器末端骨架。只有這些骨架在命中區間碰到玩家才會造成傷害。")]
    [SerializeField] private Transform[] entityAttackBones = System.Array.Empty<Transform>();

    [HideInInspector]
    [InspectorName("實體骨架命中厚度")]
    [Tooltip("每個實體攻擊骨架周圍的 X/Z 平面命中厚度，不是敵人整體的攻擊範圍。")]
    [SerializeField, Min(0.01f), FormerlySerializedAs("attackHitRadius")] private float entityBoneHitRadius = 0.5f;

    [HideInInspector]
    [InspectorName("實體命中開始進度")]
    [Tooltip("實體攻擊動畫從哪個進度開始啟用骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float entityHitWindowStart = 0.2f;

    [HideInInspector]
    [InspectorName("實體命中結束進度")]
    [Tooltip("實體攻擊動畫到哪個進度關閉骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float entityHitWindowEnd = 0.4f;

    [HideInInspector]
    [InspectorName("影紋攻擊骨架")]
    [Tooltip("可增加多個影紋手部、爪子或武器末端骨架。只有這些骨架在命中區間碰到玩家才會造成傷害。")]
    [SerializeField] private Transform[] bodyAttackBones = System.Array.Empty<Transform>();

    [HideInInspector]
    [InspectorName("影紋骨架命中厚度")]
    [Tooltip("每個影紋攻擊骨架周圍的 X/Z 平面命中厚度，不是影紋整體的攻擊範圍。")]
    [SerializeField, Min(0.01f)] private float bodyBoneHitRadius = 0.5f;

    [HideInInspector]
    [InspectorName("影紋命中開始進度")]
    [Tooltip("影紋攻擊動畫從哪個進度開始啟用骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float bodyHitWindowStart = 0.2f;

    [HideInInspector]
    [InspectorName("影紋命中結束進度")]
    [Tooltip("影紋攻擊動畫到哪個進度關閉骨架傷害。")]
    [SerializeField, Range(0f, 1f)] private float bodyHitWindowEnd = 0.4f;

    // 保留舊版單一命中點資料，載入後自動轉成第一個實體攻擊骨架。
    [HideInInspector, SerializeField, FormerlySerializedAs("attackHitPoint")] private Transform legacyEntityAttackBone = null;

    [HideInInspector]
    [Header("受光召喚動畫")]
    [InspectorName("實體召喚動畫狀態名稱")]
    [Tooltip("影紋首次受到光線照射時，實體要播放的完整 Animator 狀態名稱。Enemy2 預設為 Base Layer.call。")]
    [SerializeField] private string entitySummonAnimationStateName = "Base Layer.call";

    [HideInInspector]
    [InspectorName("實體召喚動畫片段")]
    [Tooltip("放入 Enemy2 的 call 動畫片段；未指定時會依狀態名稱自動尋找。")]
    [SerializeField] private AnimationClip entitySummonAnimationClip = null;

    [HideInInspector]
    [InspectorName("實體召喚後待機動畫狀態名稱")]
    [Tooltip("實體召喚動畫結束且影紋仍受光時要播放的完整 Animator 狀態名稱。Enemy2 預設為 Base Layer.stand with moster。")]
    [SerializeField] private string entitySummonedIdleAnimationStateName = "Base Layer.stand with moster";

    [HideInInspector]
    [InspectorName("影紋召喚動畫狀態名稱")]
    [Tooltip("影紋首次受到光線照射時要播放的完整 Animator 狀態名稱。moster2 預設為 Base Layer.call。")]
    [SerializeField] private string bodySummonAnimationStateName = "Base Layer.call";

    [HideInInspector]
    [InspectorName("影紋召喚動畫片段")]
    [Tooltip("放入 moster2 的 call 動畫片段；未指定時會依狀態名稱自動尋找。")]
    [SerializeField] private AnimationClip bodySummonAnimationClip = null;

    [HideInInspector]
    [Header("實體攻擊骨架")]
    [InspectorName("實體攻擊動畫控制器")]
    [Tooltip("放入實體實際播放攻擊動畫的 Animator。")]
    [SerializeField, FormerlySerializedAs("attackAnimator")] private Animator entityAttackAnimator = null;

    [HideInInspector]
    [InspectorName("實體待機動畫狀態名稱")]
    [Tooltip("實體未移動、未攻擊時要播放的完整 Animator 狀態名稱。")]
    [SerializeField] private string entityIdleAnimationStateName = "Base Layer.stand";

    [HideInInspector]
    [InspectorName("實體移動動畫狀態名稱")]
    [Tooltip("實體實際移動時要播放的完整 Animator 狀態名稱。")]
    [SerializeField] private string entityWalkAnimationStateName = "Base Layer.walk";

    [HideInInspector]
    [InspectorName("實體攻擊動畫狀態名稱")]
    [Tooltip("實體要播放的完整攻擊 Animator 狀態名稱。")]
    [SerializeField, FormerlySerializedAs("attackAnimationStateName")] private string entityAttackAnimationStateName = "Base Layer.attack";

    [HideInInspector]
    [InspectorName("實體攻擊動畫片段")]
    [Tooltip("放入實體的攻擊動畫片段，用來計算攻擊與骨架命中區間。")]
    [SerializeField, FormerlySerializedAs("attackAnimationClip")] private AnimationClip entityAttackAnimationClip = null;

    [HideInInspector]
    [Header("影紋／本體攻擊骨架")]
    [InspectorName("影紋攻擊動畫控制器")]
    [Tooltip("放入影紋／本體實際播放攻擊動畫的 Animator。僅在影紋受光顯示時使用。")]
    [SerializeField] private Animator bodyAttackAnimator = null;

    [HideInInspector]
    [InspectorName("影紋待機動畫狀態名稱")]
    [Tooltip("影紋顯示但未攻擊時要播放的完整 Animator 狀態名稱。")]
    [SerializeField] private string bodyIdleAnimationStateName = "Base Layer.stand";

    [HideInInspector]
    [InspectorName("影紋攻擊動畫狀態名稱")]
    [Tooltip("影紋／本體要播放的完整攻擊 Animator 狀態名稱。")]
    [SerializeField] private string bodyAttackAnimationStateName = "Base Layer.attack1";

    [HideInInspector]
    [InspectorName("影紋攻擊動畫片段")]
    [Tooltip("放入影紋／本體的攻擊動畫片段，用來計算攻擊與骨架命中區間。")]
    [SerializeField] private AnimationClip bodyAttackAnimationClip = null;

    [HideInInspector]
    [Header("整體視覺翻轉")]
    [InspectorName("視覺整體")]
    [Tooltip("放入同時包含實體、影紋與兩者骨架的共同父物件。移動時只翻轉此物件，不翻轉碰撞與移動根物件。")]
    [SerializeField] private Transform visualRoot = null;

    [HideInInspector]
    [InspectorName("預設圖片朝右")]
    [Tooltip("勾選代表視覺整體 X 縮放為正值時面向右方；若素材左右相反可取消勾選。")]
    [SerializeField] private bool visualFacesRightWhenScaleXPositive = true;

    [HideInInspector]
    [InspectorName("移動速度")]
    [Tooltip("敵人追蹤玩家或遊蕩時在 X/Z 平面上的移動速度。")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;

    [HideInInspector]
    [InspectorName("移動區域")]
    [Tooltip("可增加多個 Collider。敵人不追蹤玩家時，會在這些區域內隨機移動。")]
    [SerializeField] private Collider[] movementAreas = System.Array.Empty<Collider>();

    [HideInInspector]
    [Header("狀態與數值")]
    [InspectorName("目前狀態")]
    [Tooltip("敵對狀態會偵測、追蹤並攻擊玩家；友好狀態只會遊蕩。")]
    [SerializeField] private EnemyStatus status = EnemyStatus.敵對狀態;

    [HideInInspector]
    [InspectorName("血量")]
    [Tooltip("敵人實體與影紋本體各自使用此最大血量。")]
    [SerializeField, Min(1)] private int health = 100;

    [HideInInspector]
    [InspectorName("攻擊力")]
    [Tooltip("實體與影紋本體的每次骨架命中傷害。")]
    [SerializeField, Min(0)] private int attackPower = 10;

    [HideInInspector]
    [InspectorName("實體復活時間")]
    [Tooltip("實體血量歸零後，在原地等待多久才復活。")]
    [SerializeField, Min(0f)] private float reviveDelay = 3f;

    [HideInInspector]
    [Header("玩家攻擊受擊範圍")]
    [InspectorName("敵人實體")]
    [Tooltip("放入敵人實體物件；玩家會以圓形距離判定是否命中。")]
    [SerializeField] private GameObject entity = null;

    [HideInInspector]
    [InspectorName("實體受擊半徑")]
    [Tooltip("以實體為中心的圓形受擊範圍半徑。")]
    [SerializeField, Min(0.01f)] private float entityHitRadius = 0.75f;

    [HideInInspector]
    [InspectorName("NPC 型態")]
    [Tooltip("放入友好狀態時要替換實體顯示的 NPC Sprite。")]
    [SerializeField] private Sprite normalNpcSprite = null;

    [HideInInspector]
    // 舊場景的 NPC 物件保留為隱藏參考，避免與新的 NPC Sprite 同時顯示。
    [SerializeField] private GameObject normalNpc = null;

    [HideInInspector]
    [Header("怪物本體與光源")]
    [InspectorName("怪物本體")]
    [Tooltip("放入身後的影紋／怪物本體；僅在受到指定光源照射時顯示。")]
    [SerializeField] private GameObject body = null;

    [HideInInspector]
    [InspectorName("本體受擊半徑")]
    [Tooltip("以本體為中心的圓形受擊範圍半徑；本體受光顯示時才會生效。")]
    [SerializeField, Min(0.01f)] private float bodyHitRadius = 0.75f;

    [HideInInspector]
    [InspectorName("偵測光源")]
    [Tooltip("可增加多個 Light；本體在任一光源照射範圍內時顯示。")]
    [SerializeField] private Light[] lightSources = System.Array.Empty<Light>();
#pragma warning restore CS0414

    private void Awake()
    {
        if (!enabled) return;
        MigrateToIndependentScripts();
        enabled = false;
    }

    /// <summary>只建立缺少的元件並複製其舊設定，已存在的元件設定不會被覆寫。</summary>
    public Component[] MigrateToIndependentScripts(Func<Type, Component> createComponent = null)
    {
        Type[] types =
        {
            typeof(EnemyStateHealth), typeof(EnemyAreaMovement),
            typeof(EnemyAnimationController), typeof(EnemyPlayerAttackController)
        };
        var result = new Component[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            Component component = GetComponent(types[i]);
            if (component == null)
            {
                component = createComponent != null ? createComponent(types[i]) : gameObject.AddComponent(types[i]);
                CopyLegacySettingsTo(component);
                if (component is Behaviour behaviour) behaviour.enabled = enabled;
            }
            result[i] = component;
        }
        return result;
    }

    private void CopyLegacySettingsTo(Component destination)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        foreach (FieldInfo field in destination.GetType().GetFields(flags))
        {
            if (!field.IsDefined(typeof(SerializeField), true)) continue;
            FieldInfo source = typeof(EnemyController).GetField(field.Name, flags);
            if (source == null) continue;
            object value = source.GetValue(this);
            if (field.FieldType.IsEnum && value != null)
                value = Enum.ToObject(field.FieldType, Convert.ToInt32(value));
            field.SetValue(destination, value);
        }
    }

    // 供尚未更新的外部呼叫使用；新玩家攻擊直接尋找 EnemyStateHealth。
    public int AttackPower => GetComponent<EnemyStateHealth>() is EnemyStateHealth state ? state.AttackPower : attackPower;

    public void TakeDamage(int damage)
    {
        GetComponent<EnemyStateHealth>()?.TakeDamage(damage);
    }

    public bool TryReceivePlayerAttack(int damage, Vector3 attackPosition, float playerAttackRadius)
    {
        var state = GetComponent<EnemyStateHealth>();
        return state != null && state.TryReceivePlayerAttack(damage, attackPosition, playerAttackRadius);
    }
}
