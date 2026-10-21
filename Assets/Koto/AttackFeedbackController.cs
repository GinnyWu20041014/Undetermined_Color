using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
    public static void NotifyPlayerHit(GameObject hitObject, GameObject owner = null)
    {
        if (instance != null && instance.isActiveAndEnabled)
            instance.PlayFeedback(hitObject, owner);
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

    private void PlayFeedback(GameObject hitObject, GameObject owner)
    {
        if (hitObject == null || (!HasEnemyTag(hitObject) && !HasEnemyTag(owner))) return;
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
        shakeUntil = 0f;
        if (whiteMaterial != null) Destroy(whiteMaterial);
        whiteMaterial = null;
        instance = null;
    }
}
