using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RainParticleEffect))]
[AddComponentMenu("Koto/移動雨區")]
public sealed class MovingRainHazard : MonoBehaviour
{
    private const float ClearedCellSize = 0.2f;
    [SerializeField, Min(0f), InspectorName("前進速度")] private float moveSpeed = 0.5f;
    [SerializeField, Range(0f, 1f), InspectorName("淋雨移動速度倍率")] private float wetSpeedMultiplier = 0.6f;
    [SerializeField, Min(0f), InspectorName("離開雨水後緩速持續秒數")] private float wetDuration = 0.3f;
    [SerializeField, Min(0.01f), InspectorName("玩家淋雨判定半徑")] private float contactRadius = 0.75f;
    [SerializeField, InspectorName("玩家淋雨判定高度")] private float contactHeight = 0.75f;

    private ParticleSystem rain;
    private ParticleSystem.Particle[] particles;
    private PlayerMovement player;
    private playerlight playerLight;
    private Vector3 direction;
    private float nextPlayerSearch;
    private float wetUntil = float.NegativeInfinity;
    private readonly HashSet<Vector2Int> clearedRainColumns = new HashSet<Vector2Int>();

    private void Awake()
    {
        rain = GetComponent<ParticleSystem>();
        direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
    }

    private void Update()
    {
        transform.position += direction * (moveSpeed * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (player == null || player.gameObject.scene != gameObject.scene)
        {
            if (player != null) player.SetRainSlow(this, 1f);
            wetUntil = float.NegativeInfinity;
            if (Time.time < nextPlayerSearch) return;
            nextPlayerSearch = Time.time + 0.5f;
            player = null;
            playerLight = null;
            foreach (var candidate in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                player = candidate;
                playerLight = player.GetComponentInParent<playerlight>();
                if (playerLight == null) playerLight = player.GetComponentInChildren<playerlight>(true);
                break;
            }
            if (player == null) return;
        }

        // RainParticleEffect 在世界座標模擬，移動發射器時已發出的雨滴仍會自然落下。
        int capacity = rain.main.maxParticles;
        if (particles == null || particles.Length < capacity)
            particles = new ParticleSystem.Particle[capacity];

        Light light = playerLight != null ? playerLight.ActiveLight : null;
        Vector3 center = player.transform.position + Vector3.up * contactHeight;
        float radiusSquared = contactRadius * contactRadius;
        bool wet = false;
        bool changed = false;
        int count = rain.GetParticles(particles);
        for (int i = 0; i < count; i++)
        {
            Vector3 position = particles[i].position;
            if (IsInLight(light, position))
            {
                RememberClearedColumn(position);
                particles[i].remainingLifetime = -1f;
                changed = true;
                continue;
            }
            if (IsRainColumnCleared(position))
            {
                particles[i].remainingLifetime = -1f;
                changed = true;
                continue;
            }
            if (wet || !player.isActiveAndEnabled) continue;

            // 檢查這一幀走過的線段，避免快速下落的雨滴跨過玩家。
            float age = particles[i].startLifetime - particles[i].remainingLifetime;
            Vector3 travel = particles[i].velocity * Mathf.Clamp(age, 0f, Time.deltaTime);
            float travelSquared = travel.sqrMagnitude;
            float fraction = travelSquared > 0f ? Mathf.Clamp01(Vector3.Dot(position - center, travel) / travelSquared) : 0f;
            Vector3 contact = position - travel * fraction;
            wet = (contact - center).sqrMagnitude <= radiusSquared && !IsInLight(light, contact);
        }
        if (changed) rain.SetParticles(particles, count);
        if (wet) wetUntil = Time.time + wetDuration;
        else if (IsInLight(light, center)) wetUntil = float.NegativeInfinity;
        player.SetRainSlow(this, player.isActiveAndEnabled && (wet || Time.time < wetUntil) ? wetSpeedMultiplier : 1f);
    }

    private static bool IsInLight(Light light, Vector3 position)
    {
        if (light == null || (light.type != LightType.Point && light.type != LightType.Spot)) return false;
        Vector3 offset = position - light.transform.position;
        float distanceSquared = offset.sqrMagnitude;
        if (distanceSquared > light.range * light.range) return false;
        if (light.type == LightType.Point) return true;
        float coneCosine = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
        return Vector3.Dot(light.transform.forward, offset) >= Mathf.Sqrt(distanceSquared) * coneCosine;
    }

    private void RememberClearedColumn(Vector3 position)
    {
        Vector2Int cell = RainCell(position);
        // Keep a small margin around the beam so newly emitted drops cannot fill tiny gaps.
        for (int x = -1; x <= 1; x++)
        for (int z = -1; z <= 1; z++)
            clearedRainColumns.Add(new Vector2Int(cell.x + x, cell.y + z));
    }

    private bool IsRainColumnCleared(Vector3 position) => clearedRainColumns.Contains(RainCell(position));

    private static Vector2Int RainCell(Vector3 position) => new Vector2Int(
        Mathf.FloorToInt(position.x / ClearedCellSize), Mathf.FloorToInt(position.z / ClearedCellSize));

    private void OnDisable()
    {
        if (player != null) player.SetRainSlow(this, 1f);
        wetUntil = float.NegativeInfinity;
    }
}
