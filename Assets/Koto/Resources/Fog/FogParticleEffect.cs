using UnityEngine;

/// <summary>Runtime controls for the reusable soft fog particle prefab.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
[AddComponentMenu("Koto/霧氣粒子效果")]
public sealed class FogParticleEffect : MonoBehaviour
{
    [Header("霧氣外觀")]
    [InspectorName("霧氣顏色")]
    [SerializeField] private Color fogColor = new Color(0.72f, 0.79f, 0.86f, 1f);

    [InspectorName("透明度")]
    [Tooltip("0 為完全透明，1 為最濃。")]
    [SerializeField, Range(0f, 1f)] private float opacity = 0.28f;

    [Header("霧氣範圍")]
    [InspectorName("範圍大小（寬、高、深）")]
    [Tooltip("粒子在此盒狀範圍內生成，預設適合 2D 遊戲地面霧。")]
    [SerializeField] private Vector3 fogRange = new Vector3(18f, 2f, 8f);

    [InspectorName("飄動速度")]
    [Tooltip("控制霧氣水平飄動及細微擾動速度。")]
    [SerializeField, Min(0f)] private float driftSpeed = 0.35f;

    [Header("玩家視野空間")]
    [InspectorName("玩家視野空間半徑")]
    [Tooltip("以玩家為中心，霧氣逐漸淡開的半徑；邊界會柔和過渡。")]
    [SerializeField, Min(0f)] private float playerClearRadius = 1.8f;

    [InspectorName("視野空間柔化寬度")]
    [Tooltip("玩家圓形空間外側的霧氣淡入距離。")]
    [SerializeField, Min(0.01f)] private float playerClearFeather = 1.1f;

    [InspectorName("自動尋找玩家")]
    [SerializeField] private bool autoFindPlayers = true;

    [InspectorName("指定玩家（選填）")]
    [Tooltip("若自動尋找無法識別玩家，可在此指定；最多支援四個玩家。")]
    [SerializeField] private Transform[] playerTargets = System.Array.Empty<Transform>();

    [Header("粒子密度")]
    [InspectorName("每秒粒子數")]
    [SerializeField, Range(1f, 500f)] private float particleDensity = 32f;

    [Header("粒子大小")]
    [InspectorName("粒子大小範圍")]
    [Tooltip("每顆霧粒子會在最小與最大尺寸之間隨機生成。")]
    [SerializeField] private Vector2 particleSizeRange = new Vector2(2.2f, 5f);

    private ParticleSystem fog;
    private ParticleSystem.Particle[] particles;
    private Color appliedColor;
    private float appliedOpacity = -1f;
    private Vector3 appliedRange;
    private float appliedSpeed = -1f;
    private float appliedDensity = -1f;
    private Vector2 appliedParticleSizeRange = Vector2.one * -1f;
    private ParticleSystemRenderer fogRenderer;
    private MaterialPropertyBlock fogProperties;
    private readonly Vector4[] playerPositions = new Vector4[4];
    private PlayerHealth[] discoveredPlayers = System.Array.Empty<PlayerHealth>();
    private float nextPlayerSearch;

    private void Awake()
    {
        fog = GetComponent<ParticleSystem>();
        fogRenderer = GetComponent<ParticleSystemRenderer>();
        fogProperties = new MaterialPropertyBlock();
        ConfigureParticleSystem();
        ApplySettings();
    }

    private void OnEnable()
    {
        if (fog == null) fog = GetComponent<ParticleSystem>();
        if (fog != null) fog.Play();
    }

    private void Update()
    {
        if (fogColor != appliedColor || opacity != appliedOpacity || fogRange != appliedRange ||
            driftSpeed != appliedSpeed || particleDensity != appliedDensity ||
            particleSizeRange != appliedParticleSizeRange)
            ApplySettings();
        UpdatePlayerClearArea();
    }

    private void ConfigureParticleSystem()
    {
        fog.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = fog.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startLifetime = new ParticleSystem.MinMaxCurve(12f, 22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.06f);
        particleSizeRange.x = Mathf.Max(0.05f, particleSizeRange.x);
        particleSizeRange.y = Mathf.Max(particleSizeRange.x, particleSizeRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(particleSizeRange.x, particleSizeRange.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
        main.gravityModifier = 0f;
        main.maxParticles = 2400;

        var emission = fog.emission;
        emission.enabled = true;
        emission.rateOverTime = particleDensity;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new ParticleSystem.Burst[0]);

        var shape = fog.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = fogRange;
        shape.randomDirectionAmount = 0f;
        shape.sphericalDirectionAmount = 0f;

        var velocity = fog.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;

        var noise = fog.noise;
        noise.enabled = true;
        noise.frequency = 0.12f;
        noise.scrollSpeed = 0.08f;
        noise.damping = true;
        noise.octaveCount = 2;

        var collision = fog.collision;
        collision.enabled = false;
        var trigger = fog.trigger;
        trigger.enabled = false;
        var trails = fog.trails;
        trails.enabled = false;
        var externalForces = fog.externalForces;
        externalForces.enabled = false;
        var lights = fog.lights;
        lights.enabled = false;
        var subEmitters = fog.subEmitters;
        subEmitters.enabled = false;
        var limitVelocity = fog.limitVelocityOverLifetime;
        limitVelocity.enabled = false;

        var colorOverLifetime = fog.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.68f, 0.18f),
                new GradientAlphaKey(0.68f, 0.82f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

        fogRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        fogRenderer.alignment = ParticleSystemRenderSpace.View;
        fogRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fogRenderer.receiveShadows = false;
        fogRenderer.enabled = true;
        fogRenderer.sortingFudge = 0f;
    }

    private void ApplySettings()
    {
        if (fog == null) return;

        fogRange.x = Mathf.Max(0.1f, fogRange.x);
        fogRange.y = Mathf.Max(0.1f, fogRange.y);
        fogRange.z = Mathf.Max(0.1f, fogRange.z);
        particleSizeRange.x = Mathf.Max(0.05f, particleSizeRange.x);
        particleSizeRange.y = Mathf.Max(particleSizeRange.x, particleSizeRange.y);
        var main = fog.main;
        main.startColor = new Color(fogColor.r, fogColor.g, fogColor.b, Mathf.Clamp01(opacity));
        main.startSize = new ParticleSystem.MinMaxCurve(particleSizeRange.x, particleSizeRange.y);

        var shape = fog.shape;
        shape.scale = fogRange;

        var emission = fog.emission;
        emission.rateOverTime = particleDensity;

        var velocity = fog.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);
        velocity.y = new ParticleSystem.MinMaxCurve(-driftSpeed * 0.08f, driftSpeed * 0.08f);
        velocity.z = new ParticleSystem.MinMaxCurve(-driftSpeed * 0.35f, driftSpeed * 0.35f);

        var noise = fog.noise;
        noise.strength = driftSpeed * 0.55f;

        // Update particles already alive so changing opacity applies immediately.
        int capacity = Mathf.Max(1, main.maxParticles);
        if (particles == null || particles.Length < capacity)
            particles = new ParticleSystem.Particle[capacity];
        int count = fog.GetParticles(particles);
        Color particleColor = new Color(fogColor.r, fogColor.g, fogColor.b, Mathf.Clamp01(opacity));
        bool updateParticleSizes = particleSizeRange != appliedParticleSizeRange;
        for (int i = 0; i < count; i++)
        {
            particles[i].startColor = particleColor;
            if (updateParticleSizes)
                particles[i].startSize = Random.Range(particleSizeRange.x, particleSizeRange.y);
        }
        fog.SetParticles(particles, count);

        appliedColor = fogColor;
        appliedOpacity = opacity;
        appliedRange = fogRange;
        appliedSpeed = driftSpeed;
        appliedDensity = particleDensity;
        appliedParticleSizeRange = particleSizeRange;
    }

    private void UpdatePlayerClearArea()
    {
        if (autoFindPlayers && Time.time >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.time + 0.5f;
            discoveredPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        }

        int count = 0;
        if (playerTargets != null)
        {
            foreach (Transform target in playerTargets)
            {
                if (target == null || !target.gameObject.activeInHierarchy || count >= playerPositions.Length)
                    continue;
                playerPositions[count++] = target.position;
            }
        }

        if (autoFindPlayers && discoveredPlayers != null)
        {
            foreach (PlayerHealth player in discoveredPlayers)
            {
                if (player == null || !player.isActiveAndEnabled || count >= playerPositions.Length)
                    continue;
                Transform target = player.transform;
                playerPositions[count++] = target.position;
            }
        }

        fogRenderer.GetPropertyBlock(fogProperties);
        for (int i = 0; i < playerPositions.Length; i++)
        {
            Vector4 position = i < count ? playerPositions[i] : Vector4.zero;
            fogProperties.SetVector("_PlayerPosition" + i, position);
        }
        fogProperties.SetFloat("_PlayerCount", count);
        fogProperties.SetFloat("_PlayerClearRadius", playerClearRadius);
        fogProperties.SetFloat("_PlayerClearFeather", playerClearFeather);
        fogProperties.SetVector("_FogRange", fogRange);
        fogProperties.SetFloat("_FogEdgeFeather", Mathf.Min(fogRange.x, fogRange.z) * 0.12f);
        fogRenderer.SetPropertyBlock(fogProperties);
    }
}
