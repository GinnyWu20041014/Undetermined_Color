using UnityEngine;

/// <summary>
/// 掛在空物件上即可建立下雨粒子；只提供雨滴大小、顏色與疏密。
/// 將物件放在降雨區域上方 12 單位處，覆蓋固定 20 × 20 單位範圍。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
[AddComponentMenu("Koto/下雨粒子效果")]
public sealed class RainParticleEffect : MonoBehaviour
{
    [Header("下雨設定")]
    [InspectorName("雨滴大小")]
    [Tooltip("雨滴寬度；雨絲長度會依粒子拉伸比例呈現。")]
    [SerializeField, Range(0.01f, 0.5f)] private float dropSize = 0.02f;

    [InspectorName("雨滴顏色")]
    [Tooltip("Alpha 控制透明度。")]
    [SerializeField] private Color rainColor = new Color(0.7f, 0.82f, 1f, 0.55f);

    [InspectorName("降雨疏密")]
    [Tooltip("每秒生成的雨滴數量；0 停止生成，現有雨滴會在 1.15 秒內消失。")]
    [SerializeField, Range(0f, 2000f)] private float density = 500f;

    // 預留觸地後的短暫彈跳時間；碰撞參數固定，不增加 Inspector 欄位。
    private const float Lifetime = 1.15f;
    private ParticleSystem rain;
    private ParticleSystemRenderer rainRenderer;
    private Material rainMaterial;
    private Material originalMaterial;
    private ParticleSystem.Particle[] particles;
    private float appliedSize;
    private float appliedDensity;
    private Color appliedColor;

    private void Awake()
    {
        rain = GetComponent<ParticleSystem>();
        rainRenderer = GetComponent<ParticleSystemRenderer>();
        Shader shader = Resources.Load<Shader>("KotoRain");
        if (shader == null)
        {
            Debug.LogError("找不到 KotoRain Shader，請保留 Assets/Koto/Resources/KotoRain.shader。", this);
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            enabled = false;
            return;
        }
        originalMaterial = rainRenderer.sharedMaterial;
        rainMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        rainRenderer.sharedMaterial = rainMaterial;

        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = rain.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = Lifetime;
        main.startSpeed = 15f;
        main.startSize3D = false;
        main.startRotation3D = false;
        main.startRotation = 0f;
        main.gravityModifier = 1f;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        var shape = rain.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        // Box 的發射方向是本地 Z 軸；旋轉後朝下，薄面仍覆蓋 X/Z 地面。
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.scale = new Vector3(20f, 20f, 0.1f);
        shape.randomDirectionAmount = 0f;
        shape.sphericalDirectionAmount = 0f;
        var velocity = rain.velocityOverLifetime;
        // 不持續強制向下速度，讓碰撞系統能將雨滴反射向上。
        velocity.enabled = false;
        var collision = rain.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.collidesWith = Physics.DefaultRaycastLayers;
        collision.enableDynamicColliders = true;
        collision.bounce = 0.12f;
        collision.dampen = 0.15f;
        collision.lifetimeLoss = 0.2f;
        collision.radiusScale = 0.5f;
        collision.minKillSpeed = 0.5f;
        collision.colliderForce = 0f;
        collision.sendCollisionMessages = false;
        var emission = rain.emission;
        emission.enabled = true;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new ParticleSystem.Burst[0]);

        rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        rainRenderer.lengthScale = 4f;
        rainRenderer.velocityScale = 0.03f;
        rainRenderer.cameraVelocityScale = 0f;
        rainRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rainRenderer.receiveShadows = false;
        ApplySettings();
    }

    private void OnEnable()
    {
        if (rainMaterial != null) rain.Play();
    }

    private void Update()
    {
        if (dropSize != appliedSize || density != appliedDensity || rainColor != appliedColor)
            ApplySettings();
    }

    private void ApplySettings()
    {
        var main = rain.main;
        main.startSize = dropSize;
        main.startColor = rainColor;
        main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(density * Lifetime) + 32);
        var emission = rain.emission;
        emission.rateOverTime = density;

        // 大小及顏色調整也套用到已生成的雨滴，不必重新播放。
        if (dropSize != appliedSize || rainColor != appliedColor)
        {
            if (particles == null || particles.Length < main.maxParticles)
                particles = new ParticleSystem.Particle[main.maxParticles];
            int count = rain.GetParticles(particles);
            for (int i = 0; i < count; i++)
            {
                particles[i].startSize = dropSize;
                particles[i].startColor = rainColor;
            }
            rain.SetParticles(particles, count);
        }
        appliedSize = dropSize;
        appliedDensity = density;
        appliedColor = rainColor;
    }

    private void OnDisable()
    {
        if (rain != null) rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDestroy()
    {
        if (rainRenderer != null && rainRenderer.sharedMaterial == rainMaterial)
            rainRenderer.sharedMaterial = originalMaterial;
        if (rainMaterial != null) Destroy(rainMaterial);
    }
}
