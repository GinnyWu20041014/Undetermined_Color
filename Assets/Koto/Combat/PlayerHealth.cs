using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家生命與圓形受擊範圍控制器。
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("玩家生命")]
    [InspectorName("最大血量")]
    [Tooltip("玩家復活時會回復至此血量。")]
    [SerializeField, Min(1)] private int maxHealth = 100;

    [InspectorName("玩家受擊半徑")]
    [Tooltip("以玩家為中心的 X/Z 平面圓形受擊範圍。")]
    [SerializeField, Min(0.01f)] private float hitRadius = 0.75f;

    [InspectorName("玩家鏡頭")]
    [Tooltip("玩家死亡時會關閉、復活時會恢復的鏡頭；未指定時自動使用 Main Camera。")]
    [SerializeField] private Camera playerCamera = null;

    [InspectorName("玩家移動腳本")]
    [Tooltip("玩家死亡時會停用、鏡頭恢復後會重新啟用；未指定時會自動尋找 PlayerMovement。")]
    [SerializeField] private PlayerMovement playerMovement = null;

    private const float RespawnDelay = 3f;

    private int currentHealth;
    private bool isDead;
    private bool cameraWasEnabled;
    private Vector3 deathPosition;
    private GameObject blackScreen;
    private Rigidbody playerRigidbody;

    /// <summary>供敵人判斷是否應停止追擊玩家。</summary>
    public bool IsDead => isDead;

    private void Awake()
    {
        currentHealth = maxHealth;
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (playerMovement == null)
        {
            playerMovement = GetComponentInParent<PlayerMovement>();
            if (playerMovement == null)
            {
                playerMovement = GetComponentInChildren<PlayerMovement>();
            }
        }

        playerRigidbody = GetComponentInParent<Rigidbody>();

        CreateBlackScreen();
    }

    /// <summary>以 X/Z 平面圓形範圍判定玩家是否受到敵人攻擊。</summary>
    public bool TryReceiveEnemyAttack(int damage, Vector3 enemyPosition, float enemyAttackRadius)
    {
        if (damage <= 0 || isDead || !IsInsideEnemyAttackRange(enemyPosition, enemyAttackRadius))
        {
            return false;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        Debug.Log($"【玩家】受到 {damage} 點傷害，目前血量：{currentHealth}。", this);
        if (currentHealth == 0)
        {
            StartCoroutine(DeathAndRespawn());
        }

        return true;
    }

    /// <summary>提供敵人判斷是否已進入玩家受擊圓形範圍。</summary>
    public bool IsInsideEnemyAttackRange(Vector3 enemyPosition, float enemyAttackRadius)
    {
        Vector3 offset = transform.position - enemyPosition;
        offset.y = 0f;
        float combinedRadius = hitRadius + enemyAttackRadius;
        return offset.sqrMagnitude <= combinedRadius * combinedRadius;
    }

    private IEnumerator DeathAndRespawn()
    {
        isDead = true;
        deathPosition = transform.position;
        cameraWasEnabled = playerCamera != null && playerCamera.enabled;
        SetPlayerMovementEnabled(false);
        SetBlackScreen(true);
        if (playerCamera != null)
        {
            playerCamera.enabled = false;
        }

        Debug.Log("【玩家】血量歸零，畫面已全黑並關閉鏡頭；3 秒後原地復活。", this);
        yield return new WaitForSeconds(RespawnDelay);

        transform.position = deathPosition;
        currentHealth = maxHealth;
        if (playerCamera != null)
        {
            playerCamera.enabled = cameraWasEnabled;
        }

        SetBlackScreen(false);
        isDead = false;
        SetPlayerMovementEnabled(true);
        Debug.Log("【玩家】已在原地復活，血量與鏡頭已恢復。", this);
    }

    private void CreateBlackScreen()
    {
        blackScreen = new GameObject("玩家死亡黑幕", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
        Canvas canvas = blackScreen.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        RectTransform rectTransform = blackScreen.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        Image image = blackScreen.GetComponent<Image>();
        image.color = Color.black;
        blackScreen.SetActive(false);
    }

    private void SetBlackScreen(bool visible)
    {
        if (blackScreen != null && blackScreen.activeSelf != visible)
        {
            blackScreen.SetActive(visible);
        }
    }

    private void SetPlayerMovementEnabled(bool enabled)
    {
        if (playerMovement != null)
        {
            playerMovement.enabled = enabled;
        }

        if (!enabled && playerRigidbody != null)
        {
            playerRigidbody.linearVelocity = new Vector3(0f, playerRigidbody.linearVelocity.y, 0f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
