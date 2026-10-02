using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家生命與圓形受擊範圍控制器。
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("玩家生命")]
    [InspectorName("最大血量")]
    [Tooltip("玩家復活時會回復至此血量。")]
    [SerializeField, Min(1)] private int maxHealth = 100;

    [InspectorName("死亡後重置整個場景")]
    [Tooltip("開啟時，死亡畫面結束後重新載入目前場景，恢復所有機關與回想的初始狀態。")]
    [SerializeField] private bool resetSceneOnDeath = true;

    [InspectorName("死亡後重生點")]
    [Tooltip("僅在關閉『死亡後重置整個場景』時使用；未指定時回到玩家開場位置。")]
    [SerializeField] private Transform respawnPoint = null;

    [InspectorName("玩家受擊半徑")]
    [Tooltip("以玩家為中心的 X/Z 平面圓形受擊範圍。")]
    [SerializeField, Min(0.01f)] private float hitRadius = 0.75f;

    [InspectorName("死亡顯示圖片")]
    [Tooltip("放入玩家死亡時要顯示的 UI Image；復活時會自動關閉。")]
    [SerializeField] private Image deathImage = null;

    [InspectorName("後處理 Volume")]
    [Tooltip("死亡暈影使用的全域 Volume；未指定時會自動尋找場景中的 Volume。")]
    [SerializeField] private Volume postProcessVolume = null;

    [Header("死亡暈影設定")]
    [InspectorName("暈影動畫時間")]
    [Tooltip("從一般畫面加深至死亡暈影的秒數；數值越小越快。")]
    [SerializeField, Min(0.01f)] private float deathVignetteDuration = 0.6f;

    [InspectorName("暈影最大強度")]
    [Tooltip("死亡暈影的黑暗強度；0 為無效果，1 為最強。")]
    [SerializeField, Range(0f, 1f)] private float deathVignetteIntensity = 1f;

    [InspectorName("暈影擴散範圍")]
    [Tooltip("控制暈影從四周往中心擴散的範圍；數值越大，覆蓋中心的範圍越廣。")]
    [SerializeField, Range(0f, 1f)] private float deathVignetteSmoothness = 0.2f;

    [InspectorName("暈影顏色")]
    [Tooltip("死亡暈影的顏色。")]
    [SerializeField] private Color deathVignetteColor = Color.black;

    [InspectorName("玩家移動腳本")]
    [Tooltip("玩家死亡時會停用、鏡頭恢復後會重新啟用；未指定時會自動尋找 PlayerMovement。")]
    [SerializeField] private PlayerMovement playerMovement = null;

    [Header("死亡停用功能")]
    [Tooltip("移動、攻擊、掃描及回想撿取／放置會自動停用。其他玩家功能可加入此清單；復活時恢復原本啟用狀態。")]
    [SerializeField] private List<MonoBehaviour> additionalDeathDisabledBehaviours = new List<MonoBehaviour>();

    [Header("低血量警示效果")]
    [InspectorName("低血量顏色")]
    [Tooltip("玩家血量低於或等於 40 時使用的閃爍顏色。")]
    [SerializeField] private Color lowHealthColor = Color.red;

    [InspectorName("低血量閃爍頻率")]
    [Tooltip("玩家血量低於或等於 40 時的閃爍速度。")]
    [SerializeField, Min(0.01f)] private float lowHealthFlashFrequency = 3f;

    [InspectorName("危急血量顏色")]
    [Tooltip("玩家血量低於或等於 20 時使用的加強閃爍顏色。")]
    [SerializeField] private Color criticalHealthColor = Color.red;

    [InspectorName("危急血量閃爍頻率")]
    [Tooltip("玩家血量低於或等於 20 時的閃爍速度。")]
    [SerializeField, Min(0.01f)] private float criticalHealthFlashFrequency = 6f;

    private const float RespawnDelay = 3f;
    private const int LowHealthThreshold = 40;
    private const int CriticalHealthThreshold = 20;

    private int currentHealth;
    private bool isDead;
    private Vector3 initialPosition;
    private Transform playerBody;
    private GameObject lowHealthScreen;
    private Image lowHealthImage;
    private Rigidbody playerRigidbody;
    private Vignette deathVignette;
    private bool vignetteWasActive;
    private bool vignetteIntensityWasOverridden;
    private float originalVignetteIntensity;
    private bool vignetteColorWasOverridden;
    private Color originalVignetteColor;
    private bool vignetteSmoothnessWasOverridden;
    private float originalVignetteSmoothness;
    private readonly Dictionary<MonoBehaviour, bool> previousBehaviourStates = new Dictionary<MonoBehaviour, bool>();
    private RigidbodyConstraints originalBodyConstraints;

    /// <summary>供敵人判斷是否應停止追擊玩家。</summary>
    public bool IsDead => isDead;
    public float HitRadius => hitRadius;

    private void Awake()
    {
        currentHealth = maxHealth;
        SetupDeathVignette();

        if (playerMovement == null)
        {
            playerMovement = GetComponentInParent<PlayerMovement>();
            if (playerMovement == null)
            {
                playerMovement = GetComponentInChildren<PlayerMovement>();
            }
        }

        playerRigidbody = GetComponentInParent<Rigidbody>();
        playerBody = playerRigidbody != null ? playerRigidbody.transform :
            (playerMovement != null ? playerMovement.transform : transform);
        initialPosition = playerBody.position;

        CreateLowHealthScreen();
        SetDeathImage(false);
    }

    private void Update()
    {
        UpdateLowHealthScreen();
    }

    /// <summary>以 X/Z 平面圓形範圍判定玩家是否受到敵人攻擊。</summary>
    public bool TryReceiveEnemyAttack(int damage, Vector3 enemyPosition, float enemyAttackRadius, Vector3? attackerPosition = null)
    {
        if (damage <= 0 || isDead || !IsInsideEnemyAttackRange(enemyPosition, enemyAttackRadius))
        {
            return false;
        }

        currentHealth = Mathf.Max(0, currentHealth - damage);
        if (currentHealth > 0 && playerMovement != null)
            playerMovement.ApplyKnockback(attackerPosition ?? enemyPosition);
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

    /// <summary>雷射等即死機關使用，直接歸零血量並進入既有死亡流程。</summary>
    public void ForceDeath()
    {
        if (isDead) return;
        currentHealth = 0;
        StartCoroutine(DeathAndRespawn());
    }

    private IEnumerator DeathAndRespawn()
    {
        isDead = true;
        StopPlayerFunctions();
        SetLowHealthScreen(false);
        yield return StartCoroutine(PlayDeathVignette());
        SetDeathImage(true);

        Debug.Log(resetSceneOnDeath
            ? "【玩家】已顯示死亡畫面；3 秒後重置整個場景。"
            : "【玩家】已顯示死亡畫面；3 秒後回到重生點。", this);
        yield return new WaitForSeconds(RespawnDelay);

        if (resetSceneOnDeath && TryReloadCurrentScene()) yield break;

        Vector3 respawnPosition = respawnPoint != null ? respawnPoint.position : initialPosition;
        playerBody.position = respawnPosition;
        if (playerRigidbody != null)
        {
            playerRigidbody.position = respawnPosition;
            if (!playerRigidbody.isKinematic)
            {
                playerRigidbody.linearVelocity = Vector3.zero;
                playerRigidbody.angularVelocity = Vector3.zero;
            }
        }
        Physics.SyncTransforms();
        currentHealth = maxHealth;
        RestoreDeathVignette();
        SetDeathImage(false);
        isDead = false;
        RestorePlayerFunctions();
        Debug.Log("【玩家】已回到重生點，血量與移動已恢復。", this);
    }

    private bool TryReloadCurrentScene()
    {
        Scene scene = gameObject.scene;
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            Debug.LogError("【玩家】場景尚未儲存，無法重新載入；本次改用重生點復活。", this);
            return false;
        }

        // Restore the runtime Volume before reloading so death darkness cannot leak.
        RestoreDeathVignette();
#if UNITY_EDITOR
        // Also support testing a saved scene that is not in the build scene list.
        if (scene.buildIndex < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
            return true;
        }
#endif
        if (!Application.CanStreamedLevelBeLoaded(scene.path))
        {
            Debug.LogError($"【玩家】場景 {scene.path} 不在啟用的建置場景清單；本次改用重生點復活。", this);
            return false;
        }
        SceneManager.LoadScene(scene.path, LoadSceneMode.Single);
        return true;
    }

    private void OnDestroy()
    {
        RestoreDeathVignette();
        if (lowHealthScreen != null) Destroy(lowHealthScreen);
    }

    private void SetupDeathVignette()
    {
        if (postProcessVolume == null)
        {
            postProcessVolume = FindFirstObjectByType<Volume>();
        }

        if (postProcessVolume == null)
        {
            Debug.LogWarning("【玩家】找不到 Post-Processing Volume，死亡時將直接進入黑屏。", this);
            return;
        }

        VolumeProfile runtimeProfile = postProcessVolume.profile;
        if (!runtimeProfile.TryGet(out deathVignette))
        {
            deathVignette = runtimeProfile.Add<Vignette>(true);
        }

        vignetteWasActive = deathVignette.active;
        vignetteIntensityWasOverridden = deathVignette.intensity.overrideState;
        originalVignetteIntensity = deathVignette.intensity.value;
        vignetteColorWasOverridden = deathVignette.color.overrideState;
        originalVignetteColor = deathVignette.color.value;
        vignetteSmoothnessWasOverridden = deathVignette.smoothness.overrideState;
        originalVignetteSmoothness = deathVignette.smoothness.value;
    }

    private IEnumerator PlayDeathVignette()
    {
        if (deathVignette == null)
        {
            yield break;
        }

        deathVignette.active = true;
        deathVignette.intensity.overrideState = true;
        deathVignette.color.overrideState = true;
        deathVignette.color.value = deathVignetteColor;
        deathVignette.smoothness.overrideState = true;
        deathVignette.smoothness.value = deathVignetteSmoothness;
        float elapsedTime = 0f;
        while (elapsedTime < deathVignetteDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsedTime / deathVignetteDuration);
            deathVignette.intensity.value = Mathf.Lerp(originalVignetteIntensity, deathVignetteIntensity, progress);
            yield return null;
        }

        deathVignette.intensity.value = deathVignetteIntensity;
    }

    private void RestoreDeathVignette()
    {
        if (deathVignette == null)
        {
            return;
        }

        deathVignette.active = vignetteWasActive;
        deathVignette.intensity.overrideState = vignetteIntensityWasOverridden;
        deathVignette.intensity.value = originalVignetteIntensity;
        deathVignette.color.overrideState = vignetteColorWasOverridden;
        deathVignette.color.value = originalVignetteColor;
        deathVignette.smoothness.overrideState = vignetteSmoothnessWasOverridden;
        deathVignette.smoothness.value = originalVignetteSmoothness;
    }

    private void CreateLowHealthScreen()
    {
        lowHealthScreen = CreateFullScreenOverlay("玩家低血量紅光", Color.red, short.MaxValue - 1);
        lowHealthImage = lowHealthScreen.GetComponent<Image>();
    }

    private static GameObject CreateFullScreenOverlay(string objectName, Color color, int sortingOrder)
    {
        GameObject overlay = new GameObject(objectName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        RectTransform rectTransform = overlay.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        overlay.GetComponent<Image>().color = color;
        overlay.SetActive(false);
        return overlay;
    }

    private void SetDeathImage(bool visible)
    {
        if (deathImage != null && deathImage.gameObject.activeSelf != visible)
        {
            deathImage.gameObject.SetActive(visible);
        }
    }

    private void UpdateLowHealthScreen()
    {
        if (isDead || currentHealth > LowHealthThreshold || lowHealthImage == null)
        {
            SetLowHealthScreen(false);
            return;
        }

        bool isCritical = currentHealth <= CriticalHealthThreshold;
        float flashSpeed = isCritical ? criticalHealthFlashFrequency : lowHealthFlashFrequency;
        float minimumAlpha = isCritical ? 0.3f : 0.1f;
        float maximumAlpha = isCritical ? 0.65f : 0.25f;
        float pulse = Mathf.PingPong(Time.unscaledTime * flashSpeed, 1f);

        Color flashColor = isCritical ? criticalHealthColor : lowHealthColor;
        flashColor.a = Mathf.Lerp(minimumAlpha, maximumAlpha, pulse);
        lowHealthImage.color = flashColor;
        SetLowHealthScreen(true);
    }

    private void SetLowHealthScreen(bool visible)
    {
        if (lowHealthScreen != null && lowHealthScreen.activeSelf != visible)
        {
            lowHealthScreen.SetActive(visible);
        }
    }

    private void StopPlayerFunctions()
    {
        previousBehaviourStates.Clear();
        foreach (MonoBehaviour behaviour in playerBody.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is PlayerMovement || behaviour is PlayerAttack ||
                behaviour is ScanningSystem || behaviour is ItemPickupSystem)
                DisablePlayerBehaviour(behaviour);
        }
        DisablePlayerBehaviour(playerMovement);
        foreach (MonoBehaviour behaviour in additionalDeathDisabledBehaviours)
            DisablePlayerBehaviour(behaviour);

        if (playerRigidbody != null)
        {
            originalBodyConstraints = playerRigidbody.constraints;
            if (!playerRigidbody.isKinematic)
            {
                playerRigidbody.linearVelocity = Vector3.zero;
                playerRigidbody.angularVelocity = Vector3.zero;
            }
            playerRigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }
    }

    private void DisablePlayerBehaviour(MonoBehaviour behaviour)
    {
        if (behaviour == null || behaviour == this || previousBehaviourStates.ContainsKey(behaviour)) return;
        previousBehaviourStates.Add(behaviour, behaviour.enabled);
        if (behaviour is ScanningSystem scanning) scanning.CloseScanningImmediately();
        behaviour.enabled = false;
    }

    private void RestorePlayerFunctions()
    {
        if (playerRigidbody != null) playerRigidbody.constraints = originalBodyConstraints;
        foreach (var entry in previousBehaviourStates)
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        previousBehaviourStates.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
