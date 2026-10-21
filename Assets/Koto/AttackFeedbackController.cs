using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

/// <summary>掛在場景空物件上；玩家確實命中 Enemy 時閃白命中部位並震動相機。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/攻擊回饋控制器")]
public sealed class AttackFeedbackController : MonoBehaviour
{
    [Header("敵人判定")]
    [SerializeField] private string enemyTag = "Enemy";
    [Header("受擊閃白")]
    [SerializeField, Min(0f)] private float flashDuration = 0.1f;
    [Header("畫面震動")]
    [Tooltip("留空時使用 MainCamera。")]
    [SerializeField] private Camera targetCamera;
    [SerializeField, Min(0f)] private float shakeDuration = 0.15f;
    [SerializeField, Min(0f)] private float shakeStrength = 0.08f;
    [SerializeField, Min(1f)] private float shakeFrequency = 35f;

    [Header("彈跳傷害數字")]
    [SerializeField] private bool showDamageNumbers = true;
    [Tooltip("留空使用專案預設 TMP 字型。")]
    [SerializeField] private TMP_FontAsset damageFont;
    [SerializeField, Min(1f)] private float damageFontSize = 24f;
    [SerializeField, Min(0.1f)] private float numberDuration = 0.7f;
    [Tooltip("數字出現位置相對受傷物件的世界座標偏移。")]
    [SerializeField] private Vector3 numberWorldOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0f)] private float numberBounceHeight = 45f;
    [SerializeField] private Color enemyDamageColor = Color.white;
    [SerializeField] private Color playerDamageColor = Color.red;
    [SerializeField, Min(1)] private int maxDamageNumbers = 64;

    private sealed class DamageNumber
    {
        public TextMeshProUGUI Label;
        public Vector3 Position;
        public Color Color;
        public float Started;
        public float Duration;
        public float Drift;
    }

    private readonly List<DamageNumber> numbers = new List<DamageNumber>();
    private readonly Stack<TextMeshProUGUI> numberPool = new Stack<TextMeshProUGUI>();
    private Canvas numberCanvas;
    private RectTransform numberCanvasRect;

    private sealed class Flash
    {
        public Material Original;
        public float Until;
    }

    private static AttackFeedbackController instance;
    private readonly Dictionary<SpriteRenderer, Flash> flashes = new Dictionary<SpriteRenderer, Flash>();
    private readonly List<SpriteRenderer> expired = new List<SpriteRenderer>();
    private Material whiteMaterial;
    private float shakeUntil;
    private Camera shakenCamera;
    private Vector3 cameraPositionBeforeRender;

    /// <summary>其他玩家武器也可在確認命中後呼叫。owner 用於本體脫離敵人階層時辨識 Enemy Tag。</summary>
    public static void NotifyPlayerHit(GameObject hitObject, GameObject owner = null, int damage = 0)
    {
        if (instance != null && instance.isActiveAndEnabled)
            instance.PlayFeedback(hitObject, owner, damage);
    }

    /// <summary>玩家實際扣血後呼叫；包含荊棘每次持續傷害，不附加閃白與震動。</summary>
    public static void NotifyPlayerDamaged(GameObject player, int damage)
    {
        if (instance != null && instance.isActiveAndEnabled && player != null)
            instance.SpawnNumber(player.transform.position, damage, instance.playerDamageColor);
    }

    private void OnEnable()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("場景已有攻擊回饋控制器，停用重複元件。", this);
            enabled = false;
            return;
        }
        instance = this;
        Shader shader = Resources.Load<Shader>("AttackFeedbackWhite");
        if (shader != null) whiteMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        else Debug.LogWarning("找不到 AttackFeedbackWhite Shader，僅啟用畫面震動。", this);
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
        Camera.onPreCull += BeginCamera;
        Camera.onPostRender += EndCamera;
    }

    private bool HasEnemyTag(GameObject target)
    {
        if (target == null) return false;
        for (Transform current = target.transform; current != null; current = current.parent)
            if (current.gameObject.tag == enemyTag) return true;
        return false;
    }

    private void PlayFeedback(GameObject hitObject, GameObject owner, int damage)
    {
        if (hitObject == null || (!HasEnemyTag(hitObject) && !HasEnemyTag(owner))) return;
        SpawnNumber(hitObject.transform.position, damage, enemyDamageColor);
        float now = Time.unscaledTime;
        if (whiteMaterial != null && flashDuration > 0f)
        {
            foreach (SpriteRenderer sprite in hitObject.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!sprite.enabled || !sprite.gameObject.activeInHierarchy) continue;
                if (!flashes.TryGetValue(sprite, out Flash flash))
                {
                    flash = new Flash { Original = sprite.sharedMaterial };
                    flashes.Add(sprite, flash);
                }
                // 連續命中只延長時間，不把白色材質存成原材質。
                flash.Until = now + flashDuration;
                sprite.sharedMaterial = whiteMaterial;
            }
        }
        shakeUntil = now + shakeDuration;
    }

    private void LateUpdate()
    {
        expired.Clear();
        foreach (var entry in flashes)
        {
            if (entry.Key != null && Time.unscaledTime < entry.Value.Until) continue;
            RestoreSprite(entry.Key, entry.Value);
            expired.Add(entry.Key);
        }
        foreach (SpriteRenderer sprite in expired) flashes.Remove(sprite);
        UpdateNumbers();
    }

    private void SpawnNumber(Vector3 position, int damage, Color color)
    {
        if (!showDamageNumbers || damage <= 0) return;
        if (numberCanvas == null)
        {
            if (damageFont == null) damageFont = TMP_Settings.defaultFontAsset;
            if (damageFont == null) damageFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (damageFont == null) return;
            var root = new GameObject("Damage Numbers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            numberCanvas = root.GetComponent<Canvas>();
            numberCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            numberCanvas.sortingOrder = 200;
            numberCanvasRect = root.GetComponent<RectTransform>();
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }
        if (numbers.Count >= Mathf.Max(1, maxDamageNumbers)) RecycleNumber(0);
        TextMeshProUGUI label;
        if (numberPool.Count > 0) label = numberPool.Pop();
        else
        {
            var root = new GameObject("Damage", typeof(RectTransform), typeof(TextMeshProUGUI));
            root.transform.SetParent(numberCanvas.transform, false);
            label = root.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            label.rectTransform.sizeDelta = new Vector2(200f, 80f);
        }
        label.gameObject.SetActive(true);
        label.enabled = false;
        label.font = damageFont;
        label.fontSize = damageFontSize;
        label.text = damage.ToString();
        label.color = color;
        numbers.Add(new DamageNumber
        {
            Label = label, Position = position + numberWorldOffset, Color = color,
            Started = Time.unscaledTime, Duration = Mathf.Max(0.1f, numberDuration),
            Drift = Random.Range(-24f, 24f)
        });
    }

    private void UpdateNumbers()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        for (int i = numbers.Count - 1; i >= 0; i--)
        {
            DamageNumber number = numbers[i];
            float progress = (Time.unscaledTime - number.Started) / number.Duration;
            if (progress >= 1f || !showDamageNumbers)
            {
                RecycleNumber(i);
                continue;
            }
            if (targetCamera == null) { number.Label.enabled = false; continue; }
            Vector3 screen = targetCamera.WorldToScreenPoint(number.Position);
            number.Label.enabled = screen.z > 0f;
            if (screen.z <= 0f) continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(numberCanvasRect, screen, null, out Vector2 point);
            // 拋物線上彈後略微落下；每次扣血都有獨立數字，連續命中不互相覆蓋。
            point += new Vector2(number.Drift * progress, numberBounceHeight * (4f * progress - 3f * progress * progress));
            number.Label.rectTransform.anchoredPosition = point;
            number.Label.rectTransform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress * 3f)));
            Color color = number.Color;
            color.a *= 1f - Mathf.InverseLerp(0.55f, 1f, progress);
            number.Label.color = color;
        }
    }

    private void RecycleNumber(int index)
    {
        TextMeshProUGUI label = numbers[index].Label;
        label.gameObject.SetActive(false);
        numberPool.Push(label);
        numbers.RemoveAt(index);
    }

    private void RestoreSprite(SpriteRenderer sprite, Flash flash)
    {
        // 若死亡／換裝系統已換掉材質，保留它的新材質。
        if (sprite != null && sprite.sharedMaterial == whiteMaterial)
            sprite.sharedMaterial = flash.Original;
    }

    private void BeginCameraRendering(ScriptableRenderContext context, Camera camera) => BeginCamera(camera);
    private void EndCameraRendering(ScriptableRenderContext context, Camera camera) => EndCamera(camera);

    private void BeginCamera(Camera camera)
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (camera != targetCamera || shakenCamera != null || shakeDuration <= 0f ||
            shakeStrength <= 0f || Time.unscaledTime >= shakeUntil) return;
        float fade = Mathf.Clamp01((shakeUntil - Time.unscaledTime) / shakeDuration);
        float time = Time.unscaledTime * shakeFrequency;
        Vector2 offset = new Vector2(Mathf.PerlinNoise(time, 0.37f) * 2f - 1f,
            Mathf.PerlinNoise(0.73f, time) * 2f - 1f) * (shakeStrength * fade);
        shakenCamera = camera;
        cameraPositionBeforeRender = camera.transform.position;
        camera.transform.position += camera.transform.right * offset.x + camera.transform.up * offset.y;
    }

    private void EndCamera(Camera camera)
    {
        if (shakenCamera == null || camera != shakenCamera) return;
        shakenCamera.transform.position = cameraPositionBeforeRender;
        shakenCamera = null;
    }

    private void OnDisable()
    {
        if (instance != this) return;
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        Camera.onPreCull -= BeginCamera;
        Camera.onPostRender -= EndCamera;
        EndCamera(shakenCamera);
        foreach (var entry in flashes) RestoreSprite(entry.Key, entry.Value);
        flashes.Clear();
        numbers.Clear();
        numberPool.Clear();
        if (numberCanvas != null) Destroy(numberCanvas.gameObject);
        numberCanvas = null;
        numberCanvasRect = null;
        shakeUntil = 0f;
        if (whiteMaterial != null) Destroy(whiteMaterial);
        whiteMaterial = null;
        instance = null;
    }
}
