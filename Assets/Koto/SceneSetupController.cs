using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>以預置體保存設定，以目前場景的 Tag 重新連接跨物件引用。</summary>
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(-32000)]
[AddComponentMenu("Koto/場景設定控制器")]
public sealed class SceneSetupController : MonoBehaviour
{
    public enum PresetRole
    {
        [InspectorName("玩家")] Player,
        [InspectorName("敵人")] Enemy,
        [InspectorName("鏡頭")] Camera,
        [InspectorName("UI／場景支援")] Support
    }

    [Serializable]
    public sealed class Preset
    {
        [InspectorName("設定識別碼")] public string id;
        [InspectorName("種類")] public PresetRole role;
        [InspectorName("搜尋 Tag")] public string tag;
        [Tooltip("用來辨識原專案中相同種類的物件。")]
        [InspectorName("原始來源預置體")] public GameObject sourcePrefab;
        [Tooltip("完整保存腳本、數值、動畫、碰撞與子物件的預置體。")]
        [InspectorName("已保存設定預置體")] public GameObject prefab;
        [InspectorName("原始場景物件名稱")] public string sceneObjectName;
    }

    [Tooltip("基準來自 fight test map；不同敵人各自保存，使用 Enemy Tag 並依來源 Prefab 辨識。")]
    [InspectorName("玩家／敵人／鏡頭預置體設定")]
    public List<Preset> presets = new List<Preset>();
    [HideInInspector] public Vector3 cameraOffset;

#if UNITY_EDITOR
    public static event Action<SceneSetupController> EditorSetupRequested;
    private void OnValidate() => EditorSetupRequested?.Invoke(this);
#endif

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.IsPlaying(gameObject))
        {
            EditorSetupRequested?.Invoke(this);
            return;
        }
#endif
        BindSceneReferences();
    }

    private void Awake()
    {
        if (Application.IsPlaying(gameObject)) BindSceneReferences();
    }

    /// <summary>只補齊遺失或其他場景的引用；不覆寫有效的場景設定。</summary>
    public void BindSceneReferences()
    {
        Scene scene = gameObject.scene;
        if (!scene.IsValid()) return;
        if (FindInScene(scene) != this) return;
        var targets = GetTargets();
        foreach (SceneSetupActor actor in ComponentsInScene<SceneSetupActor>(scene))
            actor.Bind(targets);
    }

    public Dictionary<string, GameObject> GetTargets()
    {
        var result = new Dictionary<string, GameObject>();
        Scene scene = gameObject.scene;
        foreach (Preset preset in presets)
        {
            if (preset.role == PresetRole.Enemy) continue;
            var matches = new List<GameObject>();
            if (preset.role == PresetRole.Player || preset.role == PresetRole.Camera)
                matches = FindTaggedRoots(scene, preset.tag);
            else
                foreach (SceneSetupActor actor in ComponentsInScene<SceneSetupActor>(scene))
                    if (actor.presetId == preset.id) matches.Add(actor.gameObject);
            // 同一場景的玩家／主鏡頭必須唯一，避免默默連到錯誤的角色。
            if (matches.Count == 1) result[preset.id] = matches[0];
        }
        return result;
    }

    public static SceneSetupController FindInScene(Scene scene)
    {
        foreach (SceneSetupController controller in ComponentsInScene<SceneSetupController>(scene))
            if (controller.enabled && controller.gameObject.activeInHierarchy) return controller;
        return null;
    }

    public static List<GameObject> FindTaggedRoots(Scene scene, string tag)
    {
        var result = new List<GameObject>();
        if (string.IsNullOrEmpty(tag)) return result;
        foreach (Transform t in ComponentsInScene<Transform>(scene))
        {
            if (t.gameObject.tag != tag) continue;
            bool nested = false;
            for (Transform parent = t.parent; parent != null; parent = parent.parent)
                if (parent.gameObject.tag == tag) { nested = true; break; }
            if (!nested) result.Add(t.gameObject);
        }
        return result;
    }

    public static IEnumerable<T> ComponentsInScene<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid()) yield break;
        // 場景初次載入的 Awake 期間 isLoaded 尚未為 true，但序列化物件已存在。
        // 此時必須先綁定，讓玩家 UI、血量與鏡頭的 Awake/Start 取得正確引用。
        if (!scene.isLoaded)
        {
            foreach (T component in Resources.FindObjectsOfTypeAll<T>())
                if (component != null && component.gameObject.scene == scene) yield return component;
            yield break;
        }
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true))
                yield return component;
    }
}
