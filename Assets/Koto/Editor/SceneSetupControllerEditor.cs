using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[CustomEditor(typeof(SceneSetupController))]
public sealed class SceneSetupControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("僅保存與配置玩家、敵人、鏡頭及攻擊回饋。玩家設為 Player、敵人設為 Enemy、鏡頭設為 MainCamera；拖入後補齊腳本並綁定引用。", MessageType.Info);
        DrawDefaultInspector();
        var controller = (SceneSetupController)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorUtility.IsPersistent(controller) || PrefabStageUtility.GetPrefabStage(controller.gameObject) != null))
        {
            if (GUILayout.Button("搜尋 Tag 並補齊／綁定")) SceneSetupEditor.Configure(controller);
            if (GUILayout.Button("將已保存設定重新套用到場景")) SceneSetupEditor.Configure(controller, true);
            if (GUILayout.Button("新增選取敵人的設定"))
            {
                try { SceneSetupCapture.RegisterSelectedEnemy(controller); }
                catch (Exception error) { Debug.LogError("【場景設定】" + error.Message, controller); }
            }
            if (GUILayout.Button("保存目前場景設定到預置體"))
            {
                try { SceneSetupCapture.SaveCurrentScene(controller); }
                catch (Exception error) { Debug.LogError("【場景設定】" + error.Message, controller); }
            }
        }
        EditorGUILayout.HelpBox("停止播放後，先鎖定控制器的 Inspector，再選擇場景中的 Enemy。新種類按「新增選取敵人的設定」；已登記種類按保存，或直接在 Prefab Mode 修改。", MessageType.None);
    }
}

[InitializeOnLoad]
public static class SceneSetupEditor
{
    public static bool Busy;
    private static bool queued;

    static SceneSetupEditor()
    {
        SceneSetupController.EditorSetupRequested += _ => Queue();
        EditorApplication.hierarchyChanged += Queue;
        EditorSceneManager.sceneOpened += (_, __) => Queue();
        Queue();
    }

    private static void Queue()
    {
        if (queued || Busy || EditorApplication.isPlayingOrWillChangePlaymode) return;
        queued = true;
        EditorApplication.delayCall += () =>
        {
            queued = false;
            if (Busy || EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (SceneSetupController controller in Resources.FindObjectsOfTypeAll<SceneSetupController>())
            {
                if (controller == null || !controller.isActiveAndEnabled || EditorUtility.IsPersistent(controller) ||
                    !controller.gameObject.scene.isLoaded || EditorSceneManager.IsPreviewScene(controller.gameObject.scene) ||
                    PrefabStageUtility.GetPrefabStage(controller.gameObject) != null) continue;
                Configure(controller);
            }
        };
    }

    public static void Configure(SceneSetupController controller, bool overwrite = false)
    {
        if (Busy || controller == null || Application.isPlaying) return;
        Scene scene = controller.gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded) return;
        if (SceneSetupController.ComponentsInScene<SceneSetupController>(scene).Count(c => c.isActiveAndEnabled) != 1)
        {
            Debug.LogWarning("【場景設定】同一場景只能放置一個啟用的控制器。", controller);
            return;
        }

        Busy = true;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("設定玩家、怪物與鏡頭");
        try
        {
            // 只刪除舊版本由控制器收納的額外物件；原場景物件只移除本工具的連結標記。
            foreach (SceneSetupActor legacy in SceneSetupController.ComponentsInScene<SceneSetupActor>(scene).ToArray())
            {
                if (legacy.presetId != "ui" && legacy.presetId != "volume" && legacy.presetId != "event-system") continue;
                if (legacy.transform.IsChildOf(controller.transform) &&
                    legacy.GetComponentInParent<SceneSetupController>(true) == controller)
                    Undo.DestroyObjectImmediate(legacy.gameObject);
                else
                    Undo.DestroyObjectImmediate(legacy);
            }
            if (controller.presets.Any(p => p == null || !p.IsSupported))
            {
                Undo.RecordObject(controller, "移除保存範圍外的設定");
                controller.presets.RemoveAll(p => p == null || !p.IsSupported);
                Record(controller);
            }
            if (controller.presets.Count == 0)
            {
                GameObject defaultAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SceneSetupCapture.ControllerPath);
                if (defaultAsset == null) return;
                SceneSetupController defaults = defaultAsset.GetComponent<SceneSetupController>();
                Undo.RecordObject(controller, "載入 fight test map 基準設定");
                controller.presets = defaults.presets.Select(p => new SceneSetupController.Preset
                {
                    id = p.id, role = p.role, tag = p.tag, sourcePrefab = p.sourcePrefab,
                    prefab = p.prefab, sceneObjectName = p.sceneObjectName
                }).ToList();
                controller.cameraOffset = defaults.cameraOffset;
                Record(controller);
            }
            // 玩家先配置，回饋與鏡頭配置完成後才統一處理跨物件連結。
            foreach (SceneSetupController.Preset preset in controller.presets)
            {
                if (preset.prefab == null || !preset.IsSupported) continue;
                List<GameObject> matches = FindMatches(controller, preset);
                if ((preset.role == SceneSetupController.PresetRole.Camera || preset.role == SceneSetupController.PresetRole.Feedback) && matches.Count == 0)
                {
                    var created = (GameObject)PrefabUtility.InstantiatePrefab(preset.prefab, scene);
                    Undo.RegisterCreatedObjectUndo(created, "建立鏡頭或攻擊回饋");
                    created.name = preset.sceneObjectName;
                    if (preset.role == SceneSetupController.PresetRole.Feedback)
                        Undo.SetTransformParent(created.transform, controller.transform, "組織攻擊回饋物件");
                    matches.Add(created);
                }
                if ((preset.role == SceneSetupController.PresetRole.Player || preset.role == SceneSetupController.PresetRole.Camera) && matches.Count > 1)
                {
                    Debug.LogWarning($"【場景設定】找到多個 {preset.tag}，請保留唯一的玩家／主鏡頭後再綁定。", controller);
                    continue;
                }
                foreach (GameObject match in matches)
                {
                    ApplyPreset(match, preset, overwrite);
                    if (preset.role == SceneSetupController.PresetRole.Camera &&
                        (overwrite || !match.GetComponent<SceneSetupActor>().cameraPositionInitialized))
                        PositionCamera(controller, match);
                }
            }

            var targets = controller.GetTargets();
            foreach (SceneSetupActor actor in SceneSetupController.ComponentsInScene<SceneSetupActor>(scene))
            {
                foreach (SceneSetupActor.Reference reference in actor.references)
                {
                    if (reference.owner == null || SceneSetupActor.HasSceneReference(reference.Read(), scene)) continue;
                    Object value = reference.Resolve(targets);
                    if (value == null) continue;
                    Undo.RecordObject(reference.owner, "綁定場景引用");
                    if (reference.Write(value)) Record(reference.owner);
                }
            }
        }
        finally
        {
            Undo.CollapseUndoOperations(group);
            Busy = false;
        }
    }

    private static void PositionCamera(SceneSetupController controller, GameObject camera)
    {
        var playerPreset = controller.presets.FirstOrDefault(p => p.role == SceneSetupController.PresetRole.Player);
        if (playerPreset == null) return;
        var players = SceneSetupController.FindTaggedRoots(controller.gameObject.scene, playerPreset.tag);
        if (players.Count != 1) return;
        var preset = controller.presets.First(p => p.role == SceneSetupController.PresetRole.Camera);
        Undo.RecordObject(camera.transform, "套用鏡頭角度與相對位置");
        camera.transform.SetPositionAndRotation(players[0].transform.position + controller.cameraOffset, preset.prefab.transform.rotation);
        Record(camera.transform);
        var actor = camera.GetComponent<SceneSetupActor>();
        Undo.RecordObject(actor, "記錄鏡頭初始定位");
        actor.cameraPositionInitialized = true;
        Record(actor);
    }

    public static List<GameObject> FindMatches(SceneSetupController controller, SceneSetupController.Preset preset)
    {
        Scene scene = controller.gameObject.scene;
        if (preset.role == SceneSetupController.PresetRole.Player || preset.role == SceneSetupController.PresetRole.Camera)
            return SceneSetupController.FindTaggedRoots(scene, preset.tag);
        if (preset.role == SceneSetupController.PresetRole.Enemy)
            return SceneSetupController.FindTaggedRoots(scene, preset.tag).Where(go => MatchesPreset(go, preset)).ToList();

        var marked = SceneSetupController.ComponentsInScene<SceneSetupActor>(scene)
            .Where(a => a.presetId == preset.id).Select(a => a.gameObject).ToList();
        if (marked.Count > 0) return marked;
        return SceneSetupController.ComponentsInScene<Transform>(scene)
            .Where(t => MatchesPrefab(t.gameObject, preset.sourcePrefab) ||
                (t.name == preset.sceneObjectName && HasSameRootComponents(t.gameObject, preset.prefab)))
            .Select(t => t.gameObject).ToList();
    }

    public static bool MatchesPreset(GameObject target, SceneSetupController.Preset preset)
    {
        SceneSetupActor actor = target.GetComponent<SceneSetupActor>();
        if (actor != null) return actor.presetId == preset.id;
        if (MatchesPrefab(target, preset.prefab) || MatchesPrefab(target, preset.sourcePrefab)) return true;
        // 解包後仍可依原名稱辨識；不同敵人不會只因為同為 Enemy Tag 就被套用同一組設定。
        return CleanName(target.name) == CleanName(preset.sceneObjectName);
    }

    private static string CleanName(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"\s*\((Clone|\d+)\)$", string.Empty).Trim();

    private static bool MatchesPrefab(GameObject target, GameObject source)
    {
        if (source == null) return false;
        for (GameObject current = target; current != null; current = PrefabUtility.GetCorrespondingObjectFromSource(current))
            if (current == source) return true;
        return false;
    }

    private static bool HasSameRootComponents(GameObject target, GameObject template)
    {
        return template.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is SceneSetupActor))
            .All(c => target.GetComponent(c.GetType()) != null);
    }

    public static void ApplyPreset(GameObject target, SceneSetupController.Preset preset, bool overwrite)
    {
        SceneSetupActor actor = target.GetComponent<SceneSetupActor>();
        bool firstSetup = actor == null;
        bool copyValues = overwrite || (firstSetup && preset.role != SceneSetupController.PresetRole.Feedback);
        var mapping = new Dictionary<Object, Object>();
        var changed = new List<Component>();
        var existingReferences = new Dictionary<Component, Dictionary<string, Object>>();
        Transform sourceRoot = preset.prefab.transform;

        foreach (Transform sourceNode in preset.prefab.GetComponentsInChildren<Transform>(true))
        {
            Transform targetNode = SceneSetupActor.FindPath(target.transform, SceneSetupActor.GetPath(sourceRoot, sourceNode));
            if (targetNode == null) continue;
            MapNode(sourceNode, targetNode, mapping);
            foreach (Component source in sourceNode.GetComponents<Component>())
            {
                if (source == null || source is Transform || source is SceneSetupActor) continue;
                int index = Array.IndexOf(sourceNode.GetComponents(source.GetType()), source);
                Component[] components = targetNode.GetComponents(source.GetType());
                bool added = index >= components.Length;
                Component destination = added ? Undo.AddComponent(targetNode.gameObject, source.GetType()) : components[index];
                mapping[source] = destination;
                if (!copyValues && !added) continue;
                var oldRefs = new Dictionary<string, Object>();
                if (!added)
                {
                    var serialized = new SerializedObject(destination);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference &&
                            SceneSetupActor.HasSceneReference(property.objectReferenceValue, target.scene))
                            oldRefs[property.propertyPath] = property.objectReferenceValue;
                }
                existingReferences[destination] = oldRefs;
                Undo.RecordObject(destination, "套用預置體設定");
                EditorUtility.CopySerialized(source, destination);
                changed.Add(destination);
            }
        }

        // 補齊本地引用與必要的子物件，例如獨立存放的花怪本體、攻擊點。
        foreach (Transform sourceNode in preset.prefab.GetComponentsInChildren<Transform>(true))
        foreach (Component source in sourceNode.GetComponents<Component>())
        {
            if (source == null || source is Transform || source is SceneSetupActor || !mapping.TryGetValue(source, out Object mapped)) continue;
            var destination = (Component)mapped;
            var sourceObject = new SerializedObject(source);
            var destinationObject = new SerializedObject(destination);
            var property = sourceObject.GetIterator();
            bool recorded = changed.Contains(destination);
            bool dirty = recorded;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || IsIdentity(property.propertyPath)) continue;
                SerializedProperty to = destinationObject.FindProperty(property.propertyPath);
                if (to == null) continue;
                Object value = property.objectReferenceValue;
                if (existingReferences.TryGetValue(destination, out var old) && old.TryGetValue(property.propertyPath, out Object original))
                    value = original;
                else if (!recorded && to.objectReferenceValue != null) continue;
                else if (value != null && IsInside(value, sourceRoot))
                    value = MapOrCreate(value, sourceRoot, target.transform, mapping);
                if (to.objectReferenceValue == value) continue;
                if (!dirty) Undo.RecordObject(destination, "補齊預置體引用");
                to.objectReferenceValue = value;
                dirty = true;
            }
            if (dirty)
            {
                destinationObject.ApplyModifiedPropertiesWithoutUndo();
                Record(destination);
            }
        }

        SceneSetupActor templateActor = preset.prefab.GetComponent<SceneSetupActor>();
        if (actor == null) actor = Undo.AddComponent<SceneSetupActor>(target);
        if (firstSetup || overwrite || actor.references.Any(r => r.owner == null))
        {
            Undo.RecordObject(actor, "保存場景引用規則");
            actor.presetId = preset.id;
            actor.references = templateActor.references.Select(reference => new SceneSetupActor.Reference
            {
                owner = reference.owner != null ? MapOrCreate(reference.owner, sourceRoot, target.transform, mapping) as MonoBehaviour : null,
                field = reference.field, arrayIndex = reference.arrayIndex, targetId = reference.targetId,
                targetPath = reference.targetPath, componentType = reference.componentType, componentIndex = reference.componentIndex
            }).ToList();
            Record(actor);
        }
    }

    private static Object MapOrCreate(Object source, Transform sourceRoot, Transform targetRoot, Dictionary<Object, Object> mapping)
    {
        if (mapping.TryGetValue(source, out Object mapped)) return mapped;
        Transform sourceNode = GetTransform(source);
        if (sourceNode == null || !sourceNode.IsChildOf(sourceRoot)) return source;
        Transform targetNode = SceneSetupActor.FindPath(targetRoot, SceneSetupActor.GetPath(sourceRoot, sourceNode));
        if (targetNode == null)
        {
            Transform missing = sourceNode;
            while (missing.parent != sourceRoot && SceneSetupActor.FindPath(targetRoot, SceneSetupActor.GetPath(sourceRoot, missing.parent)) == null)
                missing = missing.parent;
            Transform parent = SceneSetupActor.FindPath(targetRoot, SceneSetupActor.GetPath(sourceRoot, missing.parent));
            GameObject clone = Object.Instantiate(missing.gameObject, parent, false);
            clone.name = missing.name;
            Undo.RegisterCreatedObjectUndo(clone, "補齊預置體子物件");
            MapHierarchy(missing, clone.transform, mapping);
            RemapHierarchy(clone, mapping);
            if (mapping.TryGetValue(source, out mapped)) return mapped;
            targetNode = SceneSetupActor.FindPath(targetRoot, SceneSetupActor.GetPath(sourceRoot, sourceNode));
        }
        if (targetNode == null) return null;
        MapNode(sourceNode, targetNode, mapping);
        return mapping.TryGetValue(source, out mapped) ? mapped : null;
    }

    public static void MapHierarchy(Transform source, Transform destination, Dictionary<Object, Object> mapping)
    {
        foreach (Transform t in source.GetComponentsInChildren<Transform>(true))
        {
            Transform to = SceneSetupActor.FindPath(destination, SceneSetupActor.GetPath(source, t));
            if (to != null) MapNode(t, to, mapping);
        }
    }

    private static void MapNode(Transform source, Transform destination, Dictionary<Object, Object> mapping)
    {
        mapping[source.gameObject] = destination.gameObject;
        foreach (Component component in source.GetComponents<Component>())
        {
            if (component == null) continue;
            int index = Array.IndexOf(source.GetComponents(component.GetType()), component);
            Component[] candidates = destination.GetComponents(component.GetType());
            if (index < candidates.Length) mapping[component] = candidates[index];
        }
    }

    public static void RemapHierarchy(GameObject root, Dictionary<Object, Object> mapping)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) continue;
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && !IsIdentity(property.propertyPath) &&
                    property.objectReferenceValue != null && mapping.TryGetValue(property.objectReferenceValue, out Object replacement))
                    property.objectReferenceValue = replacement;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    public static bool IsIdentity(string path)
    {
        string root = path.Split('.')[0];
        return root == "m_GameObject" || root == "m_Script" || root == "m_Father" || root == "m_Children" ||
            root == "m_CorrespondingSourceObject" || root == "m_PrefabInstance" || root == "m_PrefabAsset";
    }

    public static Transform GetTransform(Object value)
    {
        if (value == null) return null;
        return value is Component c ? c.transform : (value as GameObject)?.transform;
    }
    public static bool IsInside(Object value, Transform root)
    {
        Transform t = GetTransform(value);
        return t != null && (t == root || t.IsChildOf(root));
    }

    public static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        if (target is Component c && !EditorSceneManager.IsPreviewScene(c.gameObject.scene)) EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
    }
}
