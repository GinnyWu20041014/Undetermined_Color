using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>雙門解謎成功後，將指定粒子射線快速淡出並關閉。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Puzzle/雙門解謎成功關閉射線")]
public sealed class PuzzleSolvedRayShutdown : MonoBehaviour
{
    [InspectorName("雙門解謎控制器")]
    [Tooltip("未指定時，自動使用同一物件上的 DualDoorPuzzleController。")]
    [SerializeField] private DualDoorPuzzleController puzzleController = null;

    [InspectorName("成功後關閉的射線")]
    [Tooltip("可添加多條 RedParticleRay；成功時先將 Brightness 降至 0，再關閉各射線物件。")]
    [SerializeField] private List<RedParticleRay> raysToDisable = new List<RedParticleRay>();

    [InspectorName("亮度淡出時間")]
    [Tooltip("Brightness 降至 0 的秒數；預設 0.25 秒，0 代表立即關閉。")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.25f;

    private bool shutdownStarted;
    private RedParticleRay[] fadingRays;
    private float[] initialBrightness;

    private void OnEnable()
    {
        if (puzzleController == null) puzzleController = GetComponent<DualDoorPuzzleController>();
        if (puzzleController == null) return;
        puzzleController.PuzzleStateChanged += HandlePuzzleStateChanged;
        HandlePuzzleStateChanged(puzzleController.IsSolved);
    }

    private void OnDisable()
    {
        if (puzzleController != null) puzzleController.PuzzleStateChanged -= HandlePuzzleStateChanged;
        StopAllCoroutines();
        // If the owner is disabled during a fade, finish shutting down the targets.
        FinishShutdown();
    }

    private void HandlePuzzleStateChanged(bool isSolved)
    {
        if (!isSolved || shutdownStarted) return;
        shutdownStarted = true;
        var uniqueRays = new HashSet<RedParticleRay>();
        var targets = new List<RedParticleRay>();
        foreach (RedParticleRay ray in raysToDisable)
            if (ray != null && uniqueRays.Add(ray)) targets.Add(ray);

        fadingRays = targets.ToArray();
        initialBrightness = new float[fadingRays.Length];
        for (int i = 0; i < fadingRays.Length; i++)
            initialBrightness[i] = Mathf.Max(0f, fadingRays[i].brightness);

        StartCoroutine(FadeAndShutdown());
    }

    private IEnumerator FadeAndShutdown()
    {
        float duration = Mathf.Max(0f, fadeDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < fadingRays.Length; i++)
                if (fadingRays[i] != null)
                    fadingRays[i].brightness = Mathf.Lerp(initialBrightness[i], 0f, progress);
            yield return null;
        }
        FinishShutdown();
    }

    private void FinishShutdown()
    {
        if (fadingRays == null) return;
        RedParticleRay[] targets = fadingRays;
        fadingRays = null;
        initialBrightness = null;
        // Set every brightness first, including if a target contains this script.
        foreach (RedParticleRay ray in targets)
            if (ray != null) ray.brightness = 0f;
        foreach (RedParticleRay ray in targets)
            if (ray != null) ray.gameObject.SetActive(false);
    }
}
