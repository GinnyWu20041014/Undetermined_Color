using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A bounded, glowing beam rendered by a mesh particle along local +X.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class RedParticleRay : MonoBehaviour
{
    [Header("Ray Appearance / 射線外觀")]
    [Tooltip("亮暗：0 關閉，1 標準亮度；更高數值可搭配 Bloom。")]
    [Range(0f, 10f)] public float brightness = 2f;
    [Tooltip("射線長度（世界單位），不能超過 Max Length。")]
    [Min(0f)] public float length = 5f;
    [Tooltip("射線寬度（世界單位）。")]
    [Min(0.001f)] public float width = 0.15f;
    [Tooltip("最大允許長度（世界單位）。")]
    [Min(0f)] public float maxLength = 10f;
    public Color rayColor = new Color(1f, 0.015f, 0.005f, 1f);
    [Tooltip("外圍光暈相對於射線寬度的倍數。")]
    [Range(1f, 6f)] public float glowWidth = 3f;
    [Tooltip("關閉 Auto Depth Sorting 時使用的固定繪製順序。")]
    public int sortingOrder = 20;
    [Tooltip("專用粒子 Shader，保留引用以確保打包時不被移除。")]
    [SerializeField] private Shader rayShader;

    [Header("Depth Sorting / 前後遮擋")]
    [Tooltip("使用場景 AutoDepthSortManager 的鏡頭、圖層及深度排序，讓玩家在雷射前方時遮住雷射。")]
    public bool autoDepthSorting = true;
    [Tooltip("自動排序的微調值；0 按深度排序，正值偏前，負值偏後。")]
    public int depthSortingOffset = 0;

    [Header("Glow Breathing / 光暈呼吸")]
    [Tooltip("播放時讓光暈寬度平滑地增加與減少；關閉時使用上方 Glow Width。")]
    public bool enableGlowBreathing = true;
    [Tooltip("呼吸時最小的光暈寬度倍數。")]
    [Range(1f, 6f)] public float minGlowWidth = 2f;
    [Tooltip("呼吸時最大的光暈寬度倍數，不小於 Min Glow Width。")]
    [Range(1f, 6f)] public float maxGlowWidth = 4f;
    [Tooltip("一次由小變大、再變小的完整週期（秒）；越大越慢。")]
    [Min(0.1f)] public float breathingPeriod = 3f;

    private ParticleSystem particles;
    private Material rayMaterial;
    private Mesh rayMesh;
    private readonly ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[1];
    private float breathingPhase;
    private AutoDepthSortManager depthSortManager;

    private void OnEnable() { breathingPhase = 0f; EnsureParticles(); RefreshRay(); }
    private void Update()
    {
        if (Application.IsPlaying(gameObject) && enableGlowBreathing)
            breathingPhase = Mathf.Repeat(breathingPhase + Time.deltaTime / Mathf.Max(0.1f, breathingPeriod), 1f);
        else
            breathingPhase = 0f;
        EnsureParticles();
        RefreshRay();
    }
    private void LateUpdate()
    {
        if (particles == null || !autoDepthSorting) return;
        if (depthSortManager == null || !depthSortManager.isActiveAndEnabled)
            depthSortManager = FindFirstObjectByType<AutoDepthSortManager>();
        if (depthSortManager != null)
            depthSortManager.ApplyDepthSorting(particles.GetComponent<ParticleSystemRenderer>(), transform.position, depthSortingOffset);
    }

    private void OnValidate()
    {
        maxLength = Mathf.Max(0f, maxLength);
        length = Mathf.Clamp(length, 0f, maxLength);
        width = Mathf.Max(0.001f, width);
        brightness = Mathf.Clamp(brightness, 0f, 10f);
        glowWidth = Mathf.Clamp(glowWidth, 1f, 6f);
        minGlowWidth = Mathf.Clamp(minGlowWidth, 1f, 6f);
        maxGlowWidth = Mathf.Clamp(maxGlowWidth, minGlowWidth, 6f);
        breathingPeriod = Mathf.Max(0.1f, breathingPeriod);
    }

    private void EnsureParticles()
    {
        if (rayShader == null) rayShader = Shader.Find("UndeterminedColor/RedParticleRay");
        if (particles != null || rayShader == null) return;
        var child = new GameObject("Red Ray Particles (Generated)");
        child.hideFlags = HideFlags.HideAndDontSave;
        child.transform.SetParent(transform, false);
        particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startSize3D = true;
        main.maxParticles = 1;
        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = false;

        // Crossed quads keep the beam visible from above and from the side.
        rayMesh = new Mesh { name = "Red Ray Cross", hideFlags = HideFlags.HideAndDontSave };
        rayMesh.vertices = new[] {
            new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0),
            new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0),
            new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f),
            new Vector3(.5f,0,.5f), new Vector3(-.5f,0,.5f) };
        rayMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
            Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        rayMesh.triangles = new[] { 0,1,2,0,2,3,4,5,6,4,6,7 };
        rayMesh.RecalculateBounds();
        rayMaterial = new Material(rayShader) { hideFlags = HideFlags.HideAndDontSave };
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.mesh = rayMesh;
        renderer.sharedMaterial = rayMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
        particles.Pause();
    }

    private void RefreshRay()
    {
        if (particles == null) return;
        // Cancel inherited scale so Length/Width remain world-unit limits.
        Vector3 scale = transform.lossyScale;
        particles.transform.localScale = new Vector3(InverseScale(scale.x), InverseScale(scale.y), InverseScale(scale.z));
        float actualLength = Mathf.Clamp(length, 0f, Mathf.Max(0f, maxLength));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sortingOrder = sortingOrder;
        rayMaterial.SetColor("_RayColor", rayColor);
        rayMaterial.SetFloat("_Brightness", Mathf.Clamp(brightness, 0f, 10f));
        float currentGlowWidth = Mathf.Clamp(glowWidth, 1f, 6f);
        if (Application.IsPlaying(gameObject) && enableGlowBreathing)
        {
            float minimum = Mathf.Clamp(minGlowWidth, 1f, 6f);
            float maximum = Mathf.Clamp(maxGlowWidth, minimum, 6f);
            // Cosine eases to a stop at both ends, with no jump when the cycle repeats.
            float blend = (1f - Mathf.Cos(breathingPhase * Mathf.PI * 2f)) * 0.5f;
            currentGlowWidth = Mathf.Lerp(minimum, maximum, blend);
        }
        rayMaterial.SetFloat("_GlowWidth", currentGlowWidth);
        buffer[0] = new ParticleSystem.Particle {
            position = Vector3.right * actualLength * .5f,
            startSize3D = new Vector3(actualLength, width * currentGlowWidth, width * currentGlowWidth),
            startColor = Color.white, startLifetime = 1000f, remainingLifetime = 1000f,
            velocity = Vector3.zero };
        particles.SetParticles(buffer, actualLength > 0f && brightness > 0f ? 1 : 0);
    }

    private static float InverseScale(float value) => Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 direction = transform.right;
        float actualLength = Mathf.Clamp(length, 0f, Mathf.Max(0f, maxLength));
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin + direction * actualLength);
        Gizmos.DrawWireSphere(origin, 0.08f);
        Gizmos.DrawWireSphere(origin + direction * actualLength, 0.08f);
        Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
        Gizmos.DrawWireSphere(origin + direction * Mathf.Max(0f, maxLength), 0.1f);
    }

    private void OnDisable()
    {
        if (particles != null) Release(particles.gameObject);
        if (rayMaterial != null) Release(rayMaterial);
        if (rayMesh != null) Release(rayMesh);
        particles = null;
        rayMaterial = null;
        rayMesh = null;
    }
    private static void Release(Object target)
    {
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
