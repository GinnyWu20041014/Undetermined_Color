using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>停止回想：放置後永久固定單一機關的當下狀態，並立即消耗。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Echo/停止回想")]
public sealed class StopEcho : MonoBehaviour, IItemPlacementListener
{
    [Header("機關搜尋")]
    [InspectorName("機關作用範圍")]
    [Tooltip("放置目標沒有雙門機關時，在此範圍內尋找最近的可停止機關。")]
    [SerializeField, Min(0.01f)] private float mechanismSearchRange = 2f;

    [Header("由下往上消散")]
    [Tooltip("指定可編輯的 Particle System Prefab；未指定時使用 Resources/StopEchoParticles。消散時間使用其 Main > Duration。")]
    [SerializeField] private ParticleSystem dissolveParticlePrefab = null;
    [SerializeField] private Color dissolveColor = new Color(0.65f, 0.9f, 1f, 1f);

    private bool hasBeenUsed;
    public bool IsConsumed => hasBeenUsed;

    private readonly List<SpriteRenderer> dissolvingSprites = new List<SpriteRenderer>();
    private readonly List<Material> originalMaterials = new List<Material>();
    private readonly List<Material> effectMaterials = new List<Material>();
    private readonly List<ParticleSystem> dissolveParticles = new List<ParticleSystem>();

    public void OnItemPlaced(GameObject placementTarget)
    {
        if (hasBeenUsed)
        {
            return;
        }

        Vector3 placementPosition = placementTarget != null
            ? placementTarget.transform.position
            : transform.position;
        DualDoorPuzzleController doorMechanism = FindDoorMechanism(placementPosition, placementTarget);
        MechanismStateController legacyMechanism = doorMechanism == null
            ? FindTargetMechanism(placementPosition, placementTarget)
            : null;
        if (doorMechanism == null && legacyMechanism == null)
        {
            Debug.LogWarning("【停止回想】放置位置附近找不到可停止的機關。", this);
            return;
        }

        hasBeenUsed = true;
        string targetName;
        if (doorMechanism != null)
        {
            doorMechanism.StopMechanismPermanently(this);
            targetName = doorMechanism.name;
        }
        else
        {
            legacyMechanism.StopMechanismPermanently(this);
            targetName = legacyMechanism.name;
        }

        Debug.Log($"【停止回想】已永久停止機關：{targetName}，回想開始消散。", this);
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (Collider2D collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        StartCoroutine(Dissolve());
    }

    private IEnumerator Dissolve()
    {
        // 放在 Resources 中，確保打包後仍能取得效果 Shader。
        Shader shader = Resources.Load<Shader>("StopEchoDissolve");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogWarning("【停止回想】消散 Shader 無法使用，直接隱藏已消耗的回想。", this);
            gameObject.SetActive(false);
            yield break;
        }

        ParticleSystem prefab = dissolveParticlePrefab;
        if (prefab == null)
        {
            GameObject resource = Resources.Load<GameObject>("StopEchoParticles");
            if (resource != null) prefab = resource.GetComponent<ParticleSystem>();
        }
        if (prefab == null)
        {
            Debug.LogWarning("【停止回想】找不到消散粒子 Prefab。", this);
            gameObject.SetActive(false);
            yield break;
        }
        foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>())
        {
            if (!sprite.enabled || sprite.sprite == null) continue;
            dissolvingSprites.Add(sprite);
            originalMaterials.Add(sprite.sharedMaterial);
            Material material = new Material(shader);
            material.SetColor("_EdgeColor", dissolveColor);
            Bounds bounds = sprite.sprite.bounds;
            float bottom = sprite.flipY ? -bounds.max.y : bounds.min.y;
            material.SetVector("_VerticalBounds", new Vector4(bottom, Mathf.Max(bounds.size.y, 0.001f), 0f, 0f));
            material.SetFloat("_Progress", 0f);
            effectMaterials.Add(material);
            sprite.sharedMaterial = material;
            ParticleSystem particles = Instantiate(prefab, sprite.transform, false);
            particles.name = "StopEchoParticles";
            particles.gameObject.SetActive(true);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.gameObject.layer = sprite.gameObject.layer;
            ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sortingLayerID = sprite.sortingLayerID;
            particleRenderer.sortingOrder = sprite.sortingOrder + 1;
            // 只配合圖片寬度調整發射帶；其餘粒子模組維持 Prefab 的設定。
            var shape = particles.shape;
            Vector3 shapeScale = shape.scale;
            shapeScale.x *= bounds.size.x;
            shape.scale = shapeScale;
            PositionEmitter(particles, sprite, 0f);
            dissolveParticles.Add(particles);
        }

        float duration = Mathf.Max(0.1f, prefab.main.duration);
        foreach (ParticleSystem particles in dissolveParticles) particles.Play(true);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            yield return null;
            float delta = (prefab.main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime)
                * prefab.main.simulationSpeed;
            float step = Mathf.Min(delta, duration - elapsed);
            elapsed += step;
            float progress = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < dissolvingSprites.Count; i++)
            {
                SpriteRenderer sprite = dissolvingSprites[i];
                if (sprite == null || sprite.sprite == null) continue;
                effectMaterials[i].SetFloat("_Progress", progress);
                if (dissolveParticles[i] != null) PositionEmitter(dissolveParticles[i], sprite, progress);
            }
        }

        foreach (ParticleSystem particles in dissolveParticles)
            if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        // 依粒子的實際狀態等待，延長 Lifetime 或加入 Trails 後不會被固定秒數截斷。
        while (dissolveParticles.Exists(particles => particles != null && particles.IsAlive(true)))
            yield return null;
        gameObject.SetActive(false);
    }

    private static void PositionEmitter(ParticleSystem particles, SpriteRenderer sprite, float progress)
    {
        Bounds bounds = sprite.sprite.bounds;
        float bottom = sprite.flipY ? -bounds.max.y : bounds.min.y;
        float x = sprite.flipX ? -bounds.center.x : bounds.center.x;
        particles.transform.localPosition = new Vector3(x, bottom + bounds.size.y * progress, bounds.center.z);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        for (int i = 0; i < dissolvingSprites.Count; i++)
        {
            if (dissolvingSprites[i] != null)
                dissolvingSprites[i].sharedMaterial = originalMaterials[i];
        }
        foreach (Material material in effectMaterials) if (material != null) Destroy(material);
        foreach (ParticleSystem particles in dissolveParticles) if (particles != null) Destroy(particles.gameObject);
        dissolvingSprites.Clear();
        originalMaterials.Clear();
        effectMaterials.Clear();
        dissolveParticles.Clear();
    }

    private DualDoorPuzzleController FindDoorMechanism(Vector3 position, GameObject placementTarget)
    {
        DualDoorPuzzleController target = FindDoorMechanismOnTarget(placementTarget);
        if (target != null)
        {
            return target;
        }

        float range = Mathf.Max(0.01f, mechanismSearchRange);
        float nearestDistanceSquared = range * range;
        DualDoorPuzzleController nearest = null;
        foreach (DualDoorPuzzleController mechanism in
                 FindObjectsByType<DualDoorPuzzleController>(FindObjectsSortMode.None))
        {
            Vector3 offset = mechanism.transform.position - position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = mechanism;
            }
        }

        return nearest;
    }

    private static DualDoorPuzzleController FindDoorMechanismOnTarget(GameObject placementTarget)
    {
        if (placementTarget == null)
        {
            return null;
        }

        DualDoorPuzzleController controller = placementTarget.GetComponent<DualDoorPuzzleController>();
        return controller != null
            ? controller
            : placementTarget.GetComponentInParent<DualDoorPuzzleController>();
    }

    public void OnItemPickedUp()
    {
        // 停止回想只在放置成功時生效，且使用後無法取回。
    }

    private MechanismStateController FindTargetMechanism(Vector3 position, GameObject placementTarget)
    {
        MechanismStateController target = FindOnPlacementTarget(placementTarget);
        if (target != null)
        {
            return target;
        }

        float nearestDistanceSquared = Mathf.Max(0.01f, mechanismSearchRange);
        nearestDistanceSquared *= nearestDistanceSquared;
        MechanismStateController nearest = null;
        foreach (MechanismStateController mechanism in
                 FindObjectsByType<MechanismStateController>(FindObjectsSortMode.None))
        {
            Vector3 offset = mechanism.transform.position - position;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = mechanism;
            }
        }

        return nearest;
    }

    private static MechanismStateController FindOnPlacementTarget(GameObject placementTarget)
    {
        if (placementTarget == null)
        {
            return null;
        }

        MechanismStateController controller = placementTarget.GetComponent<MechanismStateController>();
        if (controller == null)
        {
            controller = placementTarget.GetComponentInParent<MechanismStateController>();
        }

        return controller != null
            ? controller
            : placementTarget.GetComponentInChildren<MechanismStateController>(true);
    }
}
