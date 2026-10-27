using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>由真實場景建立完整預置體；跨場景引用另存為可重新綁定的規則。</summary>
public static class SceneSetupCapture
{
    public const string Folder = "Assets/Koto/Prefabs/SceneSetup";
    public const string ControllerPath = Folder + "/SceneSetupController.prefab";
    private const string BaselineScene = "Assets/Scenes/fight test map.unity";

    [MenuItem("Koto/場景設定/從 fight test map 保存基準預置體")]
    public static void BuildBaseline()
    {
        if (Application.isPlaying) throw new InvalidOperationException("請先停止播放再保存基準。");
        bool wasBusy = SceneSetupEditor.Busy;
        SceneSetupEditor.Busy = true;
        Scene scene = EditorSceneManager.OpenPreviewScene(BaselineScene);
        try
        {
            EnsureFolder(Folder);
            var controllerObject = new GameObject("SceneSetupController");
            SceneManager.MoveGameObjectToScene(controllerObject, scene);
            var controller = controllerObject.AddComponent<SceneSetupController>();
            var sources = new Dictionary<string, GameObject>();
            GameObject player = SceneSetupController.FindTaggedRoots(scene, "Player").Single();
            GameObject camera = SceneSetupController.FindTaggedRoots(scene, "MainCamera").Single();
            Add(controller, sources, "player", SceneSetupController.PresetRole.Player, "Player", player);
            foreach (GameObject enemy in SceneSetupController.FindTaggedRoots(scene, "Enemy"))
            {
                if (enemy.GetComponent<EnemyStateHealth>() != null)
                    Add(controller, sources, "enemy-sheep", SceneSetupController.PresetRole.Enemy, "Enemy", enemy);
                else if (enemy.GetComponent<Monster32StateHealth>() != null)
                    Add(controller, sources, "enemy-flower", SceneSetupController.PresetRole.Enemy, "Enemy", enemy);
            }
            Add(controller, sources, "camera", SceneSetupController.PresetRole.Camera, "MainCamera", camera);
            var feedback = SceneSetupController.ComponentsInScene<AttackFeedbackController>(scene).FirstOrDefault();
            if (feedback != null) Add(controller, sources, "feedback", SceneSetupController.PresetRole.Feedback, "", feedback.gameObject);
            controller.cameraOffset = camera.transform.position - player.transform.position;

            foreach (SceneSetupController.Preset preset in controller.presets)
                preset.prefab = Capture(sources[preset.id], preset.id, Folder + "/" + preset.id + ".prefab", sources);
            PrefabUtility.SaveAsPrefabAsset(controllerObject, ControllerPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"【場景設定】已保存 {controller.presets.Count} 組完整設定與控制器：{ControllerPath}");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            SceneSetupEditor.Busy = wasBusy;
        }
    }

    private static void Add(SceneSetupController controller, Dictionary<string, GameObject> sources,
        string id, SceneSetupController.PresetRole role, string tag, GameObject source)
    {
        if (sources.ContainsKey(id)) throw new InvalidOperationException($"基準場景內有多個 {id}，請分別建立不同設定。");
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
        controller.presets.Add(new SceneSetupController.Preset
        {
            id = id, role = role, tag = tag, sceneObjectName = source.name,
            sourcePrefab = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path)
        });
        sources.Add(id, source);
    }

    public static void RegisterSelectedEnemy(SceneSetupController controller)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("請先停止播放再新增敵人設定。");
        GameObject selected = Selection.activeGameObject;
        GameObject enemy = SceneSetupController.FindTaggedRoots(controller.gameObject.scene, "Enemy")
            .FirstOrDefault(go => selected != null && (selected == go || selected.transform.IsChildOf(go.transform)));
        if (enemy == null)
            throw new InvalidOperationException("請鎖定控制器的 Inspector，再選擇同一場景中根物件 Tag 為 Enemy 的敵人。");
        if (controller.presets.Any(p => p != null && p.role == SceneSetupController.PresetRole.Enemy &&
            SceneSetupEditor.MatchesPreset(enemy, p)))
            throw new InvalidOperationException("此敵人已有設定，請使用「保存目前場景設定到預置體」更新。");

        // 其他角色只提供跨物件引用規則；登記新敵人不改寫其他範本。
        var sources = new Dictionary<string, GameObject>();
        foreach (var preset in controller.presets)
        {
            if (preset == null || !preset.IsSupported) continue;
            var matches = SceneSetupEditor.FindMatches(controller, preset);
            if (matches.Count == 1) sources[preset.id] = matches[0];
        }
        string name = Regex.Replace(enemy.name, @"\s*\((Clone|\d+)\)$", string.Empty).Trim();
        string stem = "enemy-" + Regex.Replace(name, @"[^\p{L}\p{Nd}_-]+", "-").Trim('-');
        EnsureFolder(Folder);
        string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + stem + ".prefab");
        string id = Path.GetFileNameWithoutExtension(path);
        for (int suffix = 2; controller.presets.Any(p => p != null && p.id == id); suffix++)
        {
            path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + stem + "-" + suffix + ".prefab");
            id = Path.GetFileNameWithoutExtension(path);
        }
        bool wasBusy = SceneSetupEditor.Busy;
        SceneSetupEditor.Busy = true;
        try
        {
            sources.Add(id, enemy);
            GameObject saved = Capture(enemy, id, path, sources);
            Undo.RecordObject(controller, "新增敵人設定");
            Add(controller, new Dictionary<string, GameObject>(), id, SceneSetupController.PresetRole.Enemy, "Enemy", enemy);
            controller.presets.Last().prefab = saved;
            SaveController(controller);
            SceneSetupEditor.Record(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"【場景設定】已新增敵人設定：{path}", controller);
        }
        finally { SceneSetupEditor.Busy = wasBusy; }
        SceneSetupEditor.Configure(controller);
    }

    private static void SaveController(SceneSetupController controller)
    {
        string controllerPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(controller.gameObject);
        if (string.IsNullOrEmpty(controllerPath)) controllerPath = ControllerPath;
        GameObject contents = PrefabUtility.LoadPrefabContents(controllerPath);
        try
        {
            SceneSetupController saved = contents.GetComponent<SceneSetupController>();
            if (saved == null) throw new InvalidOperationException("請將控制器本身建立為獨立 Prefab，再保存設定。");
            saved.presets = controller.presets.Where(p => p != null && p.IsSupported).ToList();
            saved.cameraOffset = controller.cameraOffset;
            PrefabUtility.SaveAsPrefabAsset(contents, controllerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    public static void SaveCurrentScene(SceneSetupController controller)
    {
        if (Application.isPlaying) throw new InvalidOperationException("請先停止播放，避免保存執行中的血量或動畫狀態。");
        var sources = new Dictionary<string, GameObject>();
        foreach (SceneSetupController.Preset preset in controller.presets)
        {
            if (preset == null || !preset.IsSupported) continue;
            List<GameObject> matches = SceneSetupEditor.FindMatches(controller, preset);
            if (matches.Count == 0) continue;
            if (matches.Count > 1)
            {
                GameObject selected = Selection.activeGameObject;
                matches = matches.Where(go => selected != null && (selected == go || selected.transform.IsChildOf(go.transform))).ToList();
                if (matches.Count != 1)
                    throw new InvalidOperationException($"{preset.sceneObjectName} 有多個實例；請選擇其中一隻作為保存範本，或直接在 Prefab Mode 修改。");
            }
            if (preset.prefab == null || !AssetDatabase.GetAssetPath(preset.prefab).StartsWith("Assets/"))
                throw new InvalidOperationException($"{preset.id} 沒有可寫入的設定 Prefab。");
            sources.Add(preset.id, matches[0]);
        }
        if (sources.Count == 0) throw new InvalidOperationException("目前場景找不到符合 Tag 或來源預置體的物件。");

        bool wasBusy = SceneSetupEditor.Busy;
        SceneSetupEditor.Busy = true;
        try
        {
            foreach (SceneSetupController.Preset preset in controller.presets)
                if (sources.TryGetValue(preset.id, out GameObject source))
                    preset.prefab = Capture(source, preset.id, AssetDatabase.GetAssetPath(preset.prefab), sources);
            var playerPreset = controller.presets.FirstOrDefault(p => p.role == SceneSetupController.PresetRole.Player);
            var cameraPreset = controller.presets.FirstOrDefault(p => p.role == SceneSetupController.PresetRole.Camera);
            if (playerPreset != null && cameraPreset != null && sources.TryGetValue(playerPreset.id, out GameObject player) &&
                sources.TryGetValue(cameraPreset.id, out GameObject camera))
                controller.cameraOffset = camera.transform.position - player.transform.position;

            SaveController(controller);
            SceneSetupEditor.Record(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"【場景設定】已將 {sources.Count} 組場景設定保存回預置體。新拖入的實例將使用這些設定。", controller);
        }
        finally { SceneSetupEditor.Busy = wasBusy; }
    }

    public static GameObject Capture(GameObject source, string id, string assetPath, Dictionary<string, GameObject> sources)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        var holder = new GameObject("Scene Setup Capture");
        SceneManager.MoveGameObjectToScene(holder, preview);
        holder.SetActive(false);
        try
        {
            GameObject clone = Object.Instantiate(source, holder.transform, true);
            clone.name = source.name;
            if (PrefabUtility.IsPartOfPrefabInstance(clone))
                PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var mapping = new Dictionary<Object, Object>();
            SceneSetupEditor.MapHierarchy(source.transform, clone.transform, mapping);

            // 花怪本體在基準場景是另一個根物件；收進同一 Prefab，保留相對位置與各自骨架。
            foreach (GameObject owned in FindOwnedRoots(source, sources.Values))
            {
                GameObject child = Object.Instantiate(owned, holder.transform, true);
                child.name = owned.name;
                if (PrefabUtility.IsPartOfPrefabInstance(child))
                    PrefabUtility.UnpackPrefabInstance(child, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                child.transform.SetParent(clone.transform, true);
                SceneSetupEditor.MapHierarchy(owned.transform, child.transform, mapping);
            }
            SceneSetupEditor.RemapHierarchy(clone, mapping);
            SceneSetupActor actor = clone.GetComponent<SceneSetupActor>();
            if (actor == null) actor = clone.AddComponent<SceneSetupActor>();
            actor.presetId = id;
            actor.cameraPositionInitialized = false;
            // 尚未綁定時欄位是 null，保留上次記錄的規則；有實際引用時會以此次設定取代。
            actor.references = actor.references.Where(r => r.owner != null && r.Read() == null &&
                r.targetId != "ui" && r.targetId != "volume" && r.targetId != "event-system").ToList();

            foreach (Component component in clone.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is SceneSetupActor) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference || SceneSetupEditor.IsIdentity(property.propertyPath)) continue;
                    Object value = property.objectReferenceValue;
                    if (value == null || EditorUtility.IsPersistent(value) || SceneSetupEditor.IsInside(value, clone.transform)) continue;
                    var target = sources.FirstOrDefault(pair => SceneSetupEditor.IsInside(value, pair.Value.transform));
                    if (target.Value == null)
                    {
                        // UI、後處理等不在此次保存範圍；只清除副本的外部引用，不改動場景原物件。
                        property.objectReferenceValue = null;
                        continue;
                    }
                    if (!(component is MonoBehaviour owner))
                        throw new InvalidOperationException($"{component.GetType().Name}.{property.propertyPath} 需要外部物件；請先把此引用改成 Prefab 內部連結。");
                    Match match = Regex.Match(property.propertyPath, @"^(\w+)(?:\.Array\.data\[(\d+)\])?$");
                    if (!match.Success) throw new InvalidOperationException($"不支援的跨場景欄位：{property.propertyPath}");
                    Transform targetNode = SceneSetupEditor.GetTransform(value);
                    var reference = new SceneSetupActor.Reference
                    {
                        owner = owner, field = match.Groups[1].Value,
                        arrayIndex = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : -1,
                        targetId = target.Key, targetPath = SceneSetupActor.GetPath(target.Value.transform, targetNode),
                        componentType = value is Component ? value.GetType().AssemblyQualifiedName : string.Empty,
                        componentIndex = value is Component c ? Array.IndexOf(targetNode.GetComponents(c.GetType()), c) : 0
                    };
                    if (reference.GetField() == null) throw new InvalidOperationException($"找不到連結欄位：{owner.GetType().Name}.{reference.field}");
                    actor.references.RemoveAll(r => r.owner == owner && r.field == reference.field && r.arrayIndex == reference.arrayIndex);
                    actor.references.Add(reference);
                    property.objectReferenceValue = null;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            clone.transform.localPosition = Vector3.zero;
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(clone, assetPath);
            if (saved == null) throw new InvalidOperationException("無法保存預置體：" + assetPath);
            return saved;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static IEnumerable<GameObject> FindOwnedRoots(GameObject source, IEnumerable<GameObject> knownRoots)
    {
        var roots = new HashSet<GameObject>();
        foreach (MonoBehaviour component in source.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component is SceneSetupActor) continue;
            var serialized = new SerializedObject(component);
            foreach (string field in new[] { "entity", "body", "normalNpc", "entityAttackPoint", "bodyAttackPoint", "visualRoot" })
            {
                var property = serialized.FindProperty(field);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object value = property.objectReferenceValue;
                if (value == null || EditorUtility.IsPersistent(value) || SceneSetupEditor.IsInside(value, source.transform) ||
                    knownRoots.Any(root => SceneSetupEditor.IsInside(value, root.transform))) continue;
                Transform t = SceneSetupEditor.GetTransform(value);
                if (t != null) roots.Add(t.gameObject);
            }
        }
        return roots.Where(root => !roots.Any(other => root != other && root.transform.IsChildOf(other.transform))).ToArray();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
