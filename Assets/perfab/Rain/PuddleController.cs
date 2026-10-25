using UnityEngine;
using System.Collections.Generic;

/// <summary>場景放一個控制器，即可統一控制所有 Koto 水窪的顏色與流動速度。</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Koto/水窪控制器")]
public sealed class PuddleController : MonoBehaviour
{
    [InspectorName("水窪顏色")]
    [Tooltip("統一調整水窪顏色；Alpha 控制透明度。")]
    [SerializeField] private Color puddleColor = new Color(0.3f, 0.48f, 0.58f, 0.6f);

    [InspectorName("透明度")]
    [Tooltip("數值越大越透明：0 保留原本顏色的 Alpha，1 完全透明。同步影響水面與漣漪。")]
    [SerializeField, Range(0f, 1f)] private float transparency = 0f;

    [InspectorName("流動速度")]
    [Tooltip("控制水面流動與圓形漣漪的速度；0 為靜止。")]
    [SerializeField, Range(0f, 3f)] private float flowSpeed = 0.7f;

    [Header("圖層與前後遮擋")]
    [InspectorName("自動辨別前後圖層")]
    [Tooltip("沿用場景 AutoDepthSortManager 的鏡頭、Sorting Layer 與深度排序。")]
    [SerializeField] private bool autoDepthSorting = true;
    [InspectorName("自動排序偏移")]
    [Tooltip("0 依水窪中心深度排序；負值偏後，正值偏前。")]
    [SerializeField, Range(-32768, 32767)] private int depthSortingOffset = 0;
    [InspectorName("手動圖層名稱")]
    [Tooltip("關閉自動排序或找不到排序管理器時使用；填入已存在的 Sorting Layer 名稱。")]
    [SerializeField] private string sortingLayerName = "Default";
    [InspectorName("手動排序順序")]
    [SerializeField, Range(-32768, 32767)] private int sortingOrder = 0;

    private readonly List<MeshRenderer> puddles = new List<MeshRenderer>();
    private AutoDepthSortManager depthSortManager;
    private float nextRefreshTime;

    private static readonly int ActiveId = Shader.PropertyToID("_KotoPuddleControlActive");
    private static readonly int ColorId = Shader.PropertyToID("_KotoPuddleColor");
    private static readonly int SpeedId = Shader.PropertyToID("_KotoPuddleSpeed");
    private static PuddleController activeController;
    private Color appliedColor;
    private float appliedSpeed = -1f;
    private float appliedTransparency = -1f;

    private bool CanControlScene()
    {
        if (!gameObject.scene.IsValid()) return false;
#if UNITY_EDITOR
        // 編輯 Prefab 資產時，不覆寫已開啟場景的全域設定。
        if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null) return false;
#endif
        return true;
    }

    private void OnEnable()
    {
        nextRefreshTime = 0f;
        if (!CanControlScene()) return;
        if (activeController == null) activeController = this;
        if (activeController == this) ApplySettings();
    }

    private void LateUpdate()
    {
        if (activeController != this || !CanControlScene()) return;
        // 定期納入新放置的水窪，不需逐一綁定，也不必增加子物件。
        if (Time.realtimeSinceStartup >= nextRefreshTime)
        {
            nextRefreshTime = Time.realtimeSinceStartup + 0.5f;
            puddles.Clear();
            foreach (MeshRenderer candidate in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!candidate.gameObject.scene.IsValid()) continue;
#if UNITY_EDITOR
                if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(candidate.gameObject) != null) continue;
#endif
                Material material = candidate.sharedMaterial;
                if (material != null && material.shader != null && material.shader.name == "Koto/Puddle")
                    puddles.Add(candidate);
            }
            depthSortManager = null;
            foreach (AutoDepthSortManager manager in FindObjectsByType<AutoDepthSortManager>(FindObjectsSortMode.None))
            {
                if (!manager.isActiveAndEnabled) continue;
                depthSortManager = manager;
                break;
            }
        }

        foreach (MeshRenderer puddle in puddles)
        {
            if (puddle == null) continue;
            if (autoDepthSorting && depthSortManager != null &&
                depthSortManager.ApplyDepthSorting(puddle, puddle.transform.position, depthSortingOffset)) continue;
            puddle.sortingLayerName = sortingLayerName;
            puddle.sortingOrder = Mathf.Clamp(sortingOrder, -32768, 32767);
        }
    }

    private void Update()
    {
        if (!CanControlScene()) return;
        if (activeController == null)
        {
            activeController = this;
            ApplySettings();
        }
        if (activeController == this && (appliedColor != puddleColor || appliedSpeed != flowSpeed
            || appliedTransparency != transparency))
            ApplySettings();
    }

    private void ApplySettings()
    {
        Color waterColor = QualitySettings.activeColorSpace == ColorSpace.Linear
            ? puddleColor.linear : puddleColor;
        waterColor.a = puddleColor.a * (1f - Mathf.Clamp01(transparency));
        Shader.SetGlobalColor(ColorId, waterColor);
        Shader.SetGlobalFloat(SpeedId, Mathf.Max(0f, flowSpeed));
        Shader.SetGlobalFloat(ActiveId, 1f);
        appliedColor = puddleColor;
        appliedSpeed = flowSpeed;
        appliedTransparency = transparency;
    }

    private void OnDisable()
    {
        if (activeController != this) return;
        Shader.SetGlobalFloat(ActiveId, 0f);
        activeController = null;
    }
}
