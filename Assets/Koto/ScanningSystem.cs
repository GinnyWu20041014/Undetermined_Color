using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 切換玩家的掃描模式，並在進入時播放由內向外、退出時播放由外向內的光圈。
/// 將此元件掛在玩家物件上即可使用。
/// </summary>
public class ScanningSystem : MonoBehaviour
{
    [Header("掃描模式")]
    [Tooltip("用來進入與退出掃描模式的按鍵")]
    [SerializeField] private KeyCode scanKey = KeyCode.Q;

    [Header("光圈外觀")]
    [Tooltip("光圈擴散到的最大半徑")]
    [SerializeField, Min(0.01f)] private float scanRadius = 5f;
    [Tooltip("光圈從中心擴散至最大半徑所需時間")]
    [SerializeField, Min(0.01f)] private float expansionDuration = 0.6f;
    [SerializeField, Min(0.001f)] private float ringWidth = 0.08f;
    [SerializeField] private Color ringColor = new Color(0f, 0.9f, 1f, 0.9f);
    [SerializeField, Range(12, 128)] private int ringSegments = 64;
    [Tooltip("光圈距離玩家腳下的高度")]
    [SerializeField] private float ringHeight = 0.05f;

    [Header("輪廓貼合")]
    [Tooltip("光圈會偵測這些圖層上的 Collider，並貼合最上方的表面。")]
    [SerializeField] private LayerMask surfaceLayers = ~0;
    [Tooltip("從光圈上方多高的位置往下偵測表面。")]
    [SerializeField, Min(0.01f)] private float surfaceRayStartHeight = 10f;
    [Tooltip("往下偵測表面的最遠距離。")]
    [SerializeField, Min(0.01f)] private float surfaceRayDistance = 30f;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("掃描畫面遮罩")]
    [Tooltip("掃描模式時覆蓋畫面的暗色遮罩。Alpha 值越大，背景越暗。")]
    [SerializeField] private Color scanMaskColor = new Color(0f, 0f, 0f, 0.65f);
    [Tooltip("遮罩的 UI 顯示層級；掃描文字所在 Canvas 的排序值需高於此數值。")]
    [SerializeField] private int maskSortingOrder = 100;

    [Header("掃描專用文字")]
    [Tooltip("將物件上的文字或文字根物件拖入此處；只有掃描模式時才會顯示。")]
    [SerializeField] private GameObject[] scanOnlyTextObjects = System.Array.Empty<GameObject>();

    /// <summary>玩家目前是否處於掃描模式。</summary>
    public bool IsScanning { get; private set; }

    private LineRenderer ringRenderer;
    private Transform ringTransform;
    private GameObject scanMaskObject;
    private float expansionTimer;
    private bool isRingAnimating;
    private bool isContracting;
    private readonly RaycastHit[] surfaceHits = new RaycastHit[16];

    private void Awake()
    {
        CreateRingRenderer();
        CreateScanMask();
        SetScanPresentation(false);
        LogScanningStatus();
    }

    private void Update()
    {
        UpdateRingTransform();

        if (Input.GetKeyDown(scanKey))
        {
            SetScanning(!IsScanning);
        }

        UpdateRingVisual();
    }

    /// <summary>
    /// 由其他腳本設定掃描模式時可呼叫此方法。
    /// </summary>
    public void SetScanning(bool enabled)
    {
        if (IsScanning == enabled)
        {
            return;
        }

        IsScanning = enabled;

        if (enabled)
        {
            expansionTimer = 0f;
            isContracting = false;
            isRingAnimating = true;
            ringRenderer.enabled = true;
        }
        else
        {
            expansionTimer = 0f;
            isContracting = true;
            isRingAnimating = true;
            ringRenderer.enabled = true;
        }

        SetScanPresentation(enabled);
        LogScanningStatus();
    }

    private void CreateRingRenderer()
    {
        GameObject ringObject = new GameObject("Scanning Ring");
        ringTransform = ringObject.transform;
        UpdateRingTransform();

        ringRenderer = ringObject.AddComponent<LineRenderer>();
        ringRenderer.useWorldSpace = false;
        ringRenderer.loop = true;
        ringRenderer.alignment = LineAlignment.View;
        ringRenderer.widthMultiplier = ringWidth;
        ringRenderer.numCornerVertices = 2;
        ringRenderer.numCapVertices = 2;
        ringRenderer.enabled = false;

        Shader ringShader = Shader.Find("Sprites/Default");
        if (ringShader != null)
        {
            ringRenderer.material = new Material(ringShader);
        }
    }

    // 光圈僅跟隨玩家位置，固定使用世界座標的零旋轉，不受角色 Rotation 影響。
    private void UpdateRingTransform()
    {
        if (ringTransform == null)
        {
            return;
        }

        ringTransform.position = transform.position + Vector3.up * ringHeight;
        ringTransform.rotation = Quaternion.identity;
    }

    private void OnDestroy()
    {
        if (ringTransform != null)
        {
            Destroy(ringTransform.gameObject);
        }

        if (scanMaskObject != null)
        {
            Destroy(scanMaskObject);
        }
    }

    private void UpdateRingVisual()
    {
        if (!isRingAnimating)
        {
            return;
        }

        expansionTimer += Time.deltaTime;
        float progress = Mathf.Clamp01(expansionTimer / expansionDuration);
        float radius = isContracting
            ? Mathf.Lerp(scanRadius, 0f, progress)
            : Mathf.Lerp(0f, scanRadius, progress);
        float alpha = Mathf.Lerp(ringColor.a, 0f, progress);

        DrawRing(radius, new Color(ringColor.r, ringColor.g, ringColor.b, alpha));

        if (progress >= 1f)
        {
            isRingAnimating = false;
            ringRenderer.enabled = false;
        }
    }

    private void CreateScanMask()
    {
        scanMaskObject = new GameObject("Scanning Dark Mask", typeof(RectTransform), typeof(Canvas), typeof(Image));
        Canvas canvas = scanMaskObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = maskSortingOrder;

        RectTransform rectTransform = scanMaskObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        Image image = scanMaskObject.GetComponent<Image>();
        image.color = scanMaskColor;
        image.raycastTarget = false;
    }

    private void SetScanPresentation(bool visible)
    {
        if (scanMaskObject != null)
        {
            scanMaskObject.SetActive(visible);
        }

        if (scanOnlyTextObjects == null)
        {
            return;
        }

        foreach (GameObject textObject in scanOnlyTextObjects)
        {
            if (textObject != null)
            {
                textObject.SetActive(visible);
            }
        }
    }

    private void DrawRing(float radius, Color color)
    {
        int pointCount = ringSegments + 1;
        ringRenderer.positionCount = pointCount;
        ringRenderer.startColor = color;
        ringRenderer.endColor = color;

        for (int i = 0; i < pointCount; i++)
        {
            float angle = i * Mathf.PI * 2f / ringSegments;
            Vector3 localPosition = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            localPosition.y = GetSurfaceHeight(localPosition);
            ringRenderer.SetPosition(i, localPosition);
        }
    }

    // 以每個圓環取樣點向下偵測，令光圈可沿地形和有高度物件的頂部輪廓前進。
    private float GetSurfaceHeight(Vector3 localPosition)
    {
        Vector3 worldPosition = ringTransform.TransformPoint(localPosition);
        Vector3 rayOrigin = worldPosition + Vector3.up * surfaceRayStartHeight;
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            surfaceHits,
            surfaceRayDistance,
            surfaceLayers,
            triggerInteraction);

        RaycastHit closestHit = default;
        bool hasSurfaceHit = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = surfaceHits[i].collider;
            if (hitCollider == null || hitCollider.transform.root == transform.root)
            {
                continue;
            }

            if (!hasSurfaceHit || surfaceHits[i].distance < closestHit.distance)
            {
                closestHit = surfaceHits[i];
                hasSurfaceHit = true;
            }
        }

        return hasSurfaceHit ? closestHit.point.y - ringTransform.position.y : 0f;
    }

    private void LogScanningStatus()
    {
        Debug.Log($"【掃描系統】目前模式：{(IsScanning ? "掃描模式" : "非掃描模式")}", this);
    }
}
