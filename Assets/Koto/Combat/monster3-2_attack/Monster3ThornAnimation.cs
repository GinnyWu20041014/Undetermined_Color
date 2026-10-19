using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>固定生成位置，警示與長出動畫依序各播一次，播放完刪除動畫替身，留下獨立的靜態荊棘。</summary>
public sealed class Monster3ThornAnimation : MonoBehaviour
{
    private PlayableGraph graph;
    private Transform visualRoot;

    public void Play(GameObject prefab, PlayerHealth player, AnimationClip warning, AnimationClip grow,
        float radius, float slow)
    {
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
        var animator = visual.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = visual.AddComponent<Animator>();
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
        // 動畫替身完成後移除，避免最後一格殘留或 Animator 再次播放。
        if (graph.IsValid()) graph.Destroy();
        visual.SetActive(false);
        Destroy(visual);
        visualRoot = null;

        var thorn = Instantiate(prefab, transform);
        thorn.name = "Thorn";
        thorn.transform.localPosition = Vector3.zero;
        foreach (Animator remainingAnimator in thorn.GetComponentsInChildren<Animator>(true))
        {
            remainingAnimator.enabled = false;
            Destroy(remainingAnimator);
        }
        foreach (Animation legacyAnimation in thorn.GetComponentsInChildren<Animation>(true))
        {
            legacyAnimation.enabled = false;
            Destroy(legacyAnimation);
        }
        foreach (Collider collider in thorn.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Collider2D collider in thorn.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
        gameObject.AddComponent<Monster3ThornHazard>().Arm(player, radius, slow);
        Destroy(this);
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

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }

    private void LateUpdate()
    {
        // 舊荊棘素材含有場景座標曲線；不可讓動畫將視覺移離警示與命中位置。
        if (visualRoot != null) visualRoot.localPosition = Vector3.zero;
    }
}
