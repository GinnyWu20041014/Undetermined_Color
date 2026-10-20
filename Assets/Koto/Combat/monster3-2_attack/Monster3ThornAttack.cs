using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// <summary>
/// 集中管理荊棘施放、警示／長出動畫、深度排序、接觸與玩家持續傷害。
/// 怪物上的元件只在 Release 被呼叫時施放；生成物與玩家效果由同一腳本的執行中實例處理。
/// </summary>

[DisallowMultipleComponent]
[AddComponentMenu("Koto/Combat/Monster3 荊棘攻擊")]
public sealed class Monster3ThornAttack : MonoBehaviour
{
    [InspectorName("召喚物件（荊棘 Prefab）")]
    [SerializeField] private GameObject thornPrefab = null;
    [InspectorName("攻擊間隔（秒）")]
    [SerializeField, Min(0.1f)] private float attackInterval = 5f;
    [InspectorName("玩家附近生成半徑")]
    [SerializeField, Min(0.1f)] private float spawnRadius = 2f;
    [InspectorName("固定生成 Y 座標")]
    [Tooltip("警示與荊棘使用的世界座標高度，不跟隨玩家高度。")]
    [SerializeField] private float fixedSpawnY = 0f;
    [InspectorName("荊棘接觸半徑")]
    [SerializeField, Min(0.01f)] private float contactRadius = 0.45f;
    [InspectorName("減速後速度比例")]
    [SerializeField, Range(0.01f, 1f)] private float slowMultiplier = 0.5f;
    [InspectorName("警示動畫（選填）")]
    [Tooltip("未填時直接播放長出動畫，不產生警示圈。")]
    [SerializeField] private AnimationClip warningClip = null;
    [InspectorName("荊棘長出動畫")]
    [SerializeField] private AnimationClip growClip = null;
    private enum RuntimeRole { Emitter, Thorn, PlayerEffect }
    private RuntimeRole role;
    private float nextAttackAt;
    public bool Ready => role == RuntimeRole.Emitter && isActiveAndEnabled && thornPrefab != null && Time.time >= nextAttackAt;

    public void Release(PlayerHealth player)
    {
        if (!Ready || player == null || player.IsDead) return;
        nextAttackAt = Time.time + attackInterval;
        // 同一次施放的兩個點分處玩家兩側，避免完全重疊；位置不跟隨玩家。
        float angle = Random.Range(0f, Mathf.PI * 2f);
        for (int i = 0; i < 2; i++)
        {
            float direction = angle + i * Mathf.PI + Random.Range(-0.35f, 0.35f);
            float distance = Random.Range(spawnRadius * 0.4f, spawnRadius);
            Vector3 position = player.transform.position + new Vector3(Mathf.Cos(direction), 0f, Mathf.Sin(direction)) * distance;
            position.y = fixedSpawnY;
            var anchor = new GameObject("Monster3 Thorn");
            anchor.transform.position = position;
            var thorn = anchor.AddComponent<Monster3ThornAttack>();
            thorn.BeginThorn(thornPrefab, player, warningClip, growClip, contactRadius, slowMultiplier);
        }
    }

    private SortingGroup group;
    private AutoDepthSortManager manager;
    private float nextSearch;

    private PlayableGraph graph;
    private Transform visualRoot;

    private void BeginThorn(GameObject prefab, PlayerHealth player, AnimationClip warning, AnimationClip grow,
        float radius, float slow)
    {
        role = RuntimeRole.Thorn;
        group = gameObject.AddComponent<SortingGroup>();
        UpdateSorting();
        StartCoroutine(Sequence(prefab, player, warning, grow, radius, slow));
    }

    private IEnumerator Sequence(GameObject prefab, PlayerHealth player, AnimationClip warning, AnimationClip grow,
        float radius, float slow)
    {
        var visual = Instantiate(prefab, transform);
        visualRoot = visual.transform;
        visual.transform.localPosition = Vector3.zero;
        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Collider2D collider in visual.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        Vector3 originalScale = visual.transform.localScale;
        Quaternion originalRotation = visual.transform.localRotation;
        var animators = visual.GetComponentsInChildren<Animator>(true);
        Animator animator = null;
        // 優先播放子物件上的動畫，根物件的 Sprite 保留作為靜態荊棘。
        foreach (Animator candidate in animators)
        {
            candidate.enabled = false;
            if (animator == null && candidate.transform != visual.transform) animator = candidate;
        }
        if (animator == null) animator = visual.GetComponent<Animator>();
        if (animator == null) animator = visual.AddComponent<Animator>();
        bool usesAnimationChild = animator.transform != visual.transform;
        var staticRenderers = new System.Collections.Generic.List<SpriteRenderer>();
        var staticVisibility = new System.Collections.Generic.List<bool>();
        if (usesAnimationChild)
        {
            foreach (var renderer in visual.GetComponentsInChildren<SpriteRenderer>(true))
            {
                bool animated = false;
                foreach (var candidate in animators)
                    if (candidate.transform != visual.transform && renderer.transform.IsChildOf(candidate.transform))
                        animated = true;
                if (animated) continue;
                staticRenderers.Add(renderer);
                staticVisibility.Add(renderer.enabled);
                renderer.enabled = false;
            }
            // 支援 Prefab 中預設關閉的動畫子物件及其父層。
            for (Transform current = animator.transform; current != visual.transform; current = current.parent)
                current.gameObject.SetActive(true);
        }
        animator.enabled = true;
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        if (warning != null)
            yield return PlayOnce(animator, warning);
        if (grow != null) yield return PlayOnce(animator, grow);
        else
        {
            Vector3 scale = visual.transform.localScale;
            for (float elapsed = 0f; elapsed < 0.25f; elapsed += Time.deltaTime)
            {
                visual.transform.localScale = Vector3.Scale(scale, new Vector3(1f, Mathf.Clamp01(elapsed / 0.25f), 1f));
                yield return null;
            }
            visual.transform.localScale = scale;
        }
        // 保留同一個實例；只關閉動畫子物件，避免再次生成時把動畫重新帶回來。
        if (graph.IsValid()) graph.Destroy();
        foreach (Animator remainingAnimator in visual.GetComponentsInChildren<Animator>(true))
        {
            remainingAnimator.enabled = false;
            if (remainingAnimator.transform != visual.transform)
                remainingAnimator.gameObject.SetActive(false);
        }
        foreach (Animation legacyAnimation in visual.GetComponentsInChildren<Animation>(true))
        {
            legacyAnimation.enabled = false;
            if (legacyAnimation.transform != visual.transform)
                legacyAnimation.gameObject.SetActive(false);
        }
        visual.name = "Thorn";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = originalScale;
        visual.transform.localRotation = originalRotation;
        for (int i = 0; i < staticRenderers.Count; i++)
            staticRenderers[i].enabled = staticVisibility[i];
        visualRoot = null;
        ArmThorn(player, radius, slow);
    }

    private IEnumerator PlayOnce(Animator animator, AnimationClip clip)
    {
        if (graph.IsValid()) graph.Destroy();
        graph = PlayableGraph.Create("Thorn single play");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationClipPlayable.Create(graph, clip);
        var output = AnimationPlayableOutput.Create(graph, "Thorn", animator);
        output.SetSourcePlayable(playable);
        graph.Play();
        // 手動取樣且不越過最後一格，忽略素材的 Loop Time 設定。
        float lastFrame = Mathf.Max(0f, clip.length - 1f / Mathf.Max(1f, clip.frameRate));
        for (float elapsed = 0f; elapsed < clip.length; elapsed += Time.deltaTime)
        {
            playable.SetTime(Mathf.Min(elapsed, lastFrame));
            graph.Evaluate(0f);
            yield return null;
        }
        playable.SetTime(lastFrame);
        graph.Evaluate(0f);
    }

    private PlayerHealth thornTarget;
    private float radius;
    private float slow;
    private float thornExpiresAt;
    private bool armed;

    private void ArmThorn(PlayerHealth target, float contactRadius, float speedMultiplier)
    {
        thornTarget = target;
        radius = contactRadius;
        slow = speedMultiplier;
        thornExpiresAt = Time.time + 5f;
        armed = true;
    }

    private void UpdateHazard()
    {
        if (!armed) return;
        if (Time.time >= thornExpiresAt) { Destroy(gameObject); return; }
        if (thornTarget == null || thornTarget.IsDead || !thornTarget.IsInsideEnemyAttackRange(transform.position, radius)) return;
        armed = false;
        var status = thornTarget.GetComponent<Monster3ThornAttack>();
        if (status == null) status = thornTarget.gameObject.AddComponent<Monster3ThornAttack>();
        status.enabled = true;
        status.ApplyPlayerEffect(thornTarget, slow);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
    private PlayerHealth health;
    private PlayerMovement movement;
    private float expiresAt;
    private float nextTick;
    private bool active;

    private void ApplyPlayerEffect(PlayerHealth target, float speedMultiplier)
    {
        role = RuntimeRole.PlayerEffect;
        health = target;
        if (movement == null) movement = target.GetComponentInParent<PlayerMovement>();
        if (movement == null) movement = target.GetComponentInChildren<PlayerMovement>();
        if (!active) nextTick = Time.time + 1f;
        active = true;
        expiresAt = Time.time + 5f;
        if (movement != null) movement.ThornSpeedMultiplier = Mathf.Clamp01(speedMultiplier);
    }

    private void UpdatePlayerEffect()
    {
        if (!active) return;
        if (health == null || health.IsDead) { Clear(); return; }
        while (nextTick <= Time.time && nextTick <= expiresAt)
        {
            nextTick += 1f;
            health.ReceiveDamageOverTime(1);
            if (health.IsDead) { Clear(); return; }
        }
        if (Time.time >= expiresAt) Clear();
    }

    private void Clear()
    {
        active = false;
        if (movement != null) movement.ThornSpeedMultiplier = 1f;
    }


    private void UpdateSorting()
    {
        if ((manager == null || !manager.isActiveAndEnabled) && Time.time >= nextSearch)
        {
            nextSearch = Time.time + 0.5f;
            manager = null;
            foreach (var candidate in FindObjectsByType<AutoDepthSortManager>(FindObjectsSortMode.None))
            {
                if (!candidate.isActiveAndEnabled) continue;
                manager = candidate;
                break;
            }
        }
        if (manager != null) manager.ApplyDepthSorting(group, transform.position, 0);
    }

    private void Update()
    {
        if (role == RuntimeRole.Thorn) UpdateHazard();
        else if (role == RuntimeRole.PlayerEffect) UpdatePlayerEffect();
    }

    private void LateUpdate()
    {
        if (role != RuntimeRole.Thorn) return;
        if (visualRoot != null) visualRoot.localPosition = Vector3.zero;
        UpdateSorting();
    }

    private void OnDisable()
    {
        if (role == RuntimeRole.PlayerEffect) Clear();
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
        if (role == RuntimeRole.PlayerEffect) Clear();
    }
}
