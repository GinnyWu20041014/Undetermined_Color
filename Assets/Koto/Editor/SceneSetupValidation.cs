using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>在暫存預覽場景驗證基準保存、跨場景引用與重複配置，不修改工作場景。</summary>
public static class SceneSetupValidation
{
    private static readonly List<string> checks = new List<string>();

    [MenuItem("Koto/場景設定/驗證預置體與自動綁定")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("請先停止播放。");
        checks.Clear();
        Scene baseline = EditorSceneManager.OpenPreviewScene("Assets/Scenes/fight test map.unity");
        Scene test = EditorSceneManager.NewPreviewScene();
        Scene other = EditorSceneManager.NewPreviewScene();
        Scene latePlayerScene = EditorSceneManager.NewPreviewScene();
        bool wasBusy = SceneSetupEditor.Busy;
        Object oldSelection = Selection.activeObject;
        string temporaryFolder = "Assets/__SceneSetupValidation_" + Guid.NewGuid().ToString("N");
        SceneSetupEditor.Busy = true;
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(SceneSetupCapture.ControllerPath);
            Require(asset != null, "控制器預置體存在");
            var controller = ((GameObject)PrefabUtility.InstantiatePrefab(asset, test)).GetComponent<SceneSetupController>();
            Require(controller.presets.Count == 5 && controller.presets.All(p => p.IsSupported), "只保存玩家、兩種敵人、鏡頭與攻擊回饋");
            int scalarCount = 0;
            foreach (var preset in controller.presets)
            {
                GameObject original = baseline.GetRootGameObjects().Single(go => go.name == preset.sceneObjectName);
                scalarCount += CompareSettings(original, preset.prefab);
                ValidateAssetReferences(preset.prefab);
            }
            checks.Add($"基準逐欄位比對：{scalarCount} 個非引用序列化欄位一致；資產引用與跨物件連結記錄完整");

            var playerPreset = controller.presets.Single(p => p.id == "player");
            var sheepPreset = controller.presets.Single(p => p.id == "enemy-sheep");
            var flowerPreset = controller.presets.Single(p => p.id == "enemy-flower");
            GameObject player = Instantiate(playerPreset.prefab, test);
            player.transform.position = new Vector3(25f, 2f, 30f);
            GameObject sheep = Instantiate(sheepPreset.prefab, test);
            GameObject flower = Instantiate(flowerPreset.prefab, test);
            GameObject secondFlower = Instantiate(flowerPreset.prefab, test);
            secondFlower.SetActive(false);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            SceneManager.MoveGameObjectToScene(camera, test);
            camera.tag = "MainCamera";
            GameObject otherPlayer = Instantiate(playerPreset.prefab, other);
            Configure(controller);
            ValidateBindings(controller);
            Require(!SceneSetupController.ComponentsInScene<Canvas>(test).Any() &&
                !SceneSetupController.ComponentsInScene<UnityEngine.Rendering.Volume>(test).Any() &&
                !SceneSetupController.ComponentsInScene<UnityEngine.EventSystems.EventSystem>(test).Any(),
                "配置不會建立 UI、後處理或 EventSystem");
            Require(camera.GetComponent<Camera>().orthographic && camera.GetComponent<Camera>().orthographicSize == 5f,
                "新場景預設鏡頭已取得基準投影、角度與跟隨設定");
            Require(camera.GetComponent<CameraFollow>().player == player.transform, "鏡頭只綁定同場景玩家");
            Require(Vector3.Distance(camera.transform.position, player.transform.position + controller.cameraOffset) < 0.001f,
                "鏡頭保留基準相對玩家偏移");
            Require(flower.GetComponent<Monster32StateHealth>().Body != secondFlower.GetComponent<Monster32StateHealth>().Body,
                "多隻花怪各自保有自己的本體");
            Require(flower.GetComponent<Monster32StateHealth>().Body.transform.IsChildOf(flower.transform), "外部花怪本體已收進預置體");
            int count = SceneSetupController.ComponentsInScene<Transform>(test).Count();
            Configure(controller);
            Require(SceneSetupController.ComponentsInScene<Transform>(test).Count() == count, "重複配置不增加鏡頭或回饋物件");

            var generatedUi = new GameObject("Legacy Generated UI", typeof(RectTransform), typeof(Canvas), typeof(SceneSetupActor));
            SceneManager.MoveGameObjectToScene(generatedUi, test);
            generatedUi.transform.SetParent(controller.transform);
            generatedUi.GetComponent<SceneSetupActor>().presetId = "ui";
            var existingUi = new GameObject("Existing Scene UI", typeof(RectTransform), typeof(Canvas), typeof(SceneSetupActor));
            SceneManager.MoveGameObjectToScene(existingUi, test);
            existingUi.GetComponent<SceneSetupActor>().presetId = "ui";
            Configure(controller);
            Require(generatedUi == null && existingUi != null && existingUi.GetComponent<SceneSetupActor>() == null,
                "舊版自動建立的額外物件已清理，場景原有物件保留");
            Object.DestroyImmediate(existingUi);

            player.GetComponent<PlayerMovement>().moveSpeed = 6.25f;
            Object.DestroyImmediate(sheep.GetComponent<EnemyPlayerAttackController>());
            Object.DestroyImmediate(sheep.GetComponent<EnemyAreaMovement>());
            Configure(controller);
            Require(sheep.GetComponent<EnemyPlayerAttackController>() != null && sheep.GetComponent<EnemyAreaMovement>() != null,
                "遺失的敵人攻擊與移動元件可自動補齊");
            Require(player.GetComponent<PlayerMovement>().moveSpeed == 6.25f, "已設定物件的數值修改不被自動配置覆寫");
            ValidateBindings(controller);

            // 清空所有場景引用，直接呼叫正式執行期方法，涵蓋 inactive 敵人與陣列光源。
            foreach (var actor in SceneSetupController.ComponentsInScene<SceneSetupActor>(test))
                foreach (var reference in actor.references) Clear(reference);
            controller.BindSceneReferences();
            ValidateBindings(controller);
            Require(GetReference(sheep.GetComponent<EnemyPlayerAttackController>(), "player") == player.transform,
                "執行期重新綁定，沒有抓到另一場景的 Player Tag");

            // 使用副本測試保存按鈕，絕不覆寫交付給使用者的基準檔案。
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(temporaryFolder));
            var baselineSources = controller.presets.ToDictionary(p => p.id,
                p => baseline.GetRootGameObjects().Single(go => go.name == p.sceneObjectName));
            var capturedPlayer = SceneSetupCapture.Capture(baselineSources["player"], "player",
                temporaryFolder + "/CapturedPlayer.prefab", baselineSources);
            Require(capturedPlayer.GetComponent<SceneSetupActor>().references.Count == 1 &&
                GetReference(capturedPlayer.GetComponent<PlayerHealth>(), "deathImage") == null &&
                GetReference(capturedPlayer.GetComponent<PlayerHealth>(), "postProcessVolume") == null &&
                GetReference(capturedPlayer.GetComponent<playerlight>(), "energySlider") == null,
                "從基準保存玩家時排除 UI 與後處理引用");
            Require(GetReference(baselineSources["player"].GetComponent<PlayerHealth>(), "deathImage") != null,
                "排除保存不會清除原場景的 UI 引用");
            foreach (var preset in controller.presets)
            {
                string copyPath = temporaryFolder + "/" + preset.id + ".prefab";
                Require(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(preset.prefab), copyPath), "建立隔離保存副本：" + preset.id);
                preset.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(copyPath);
            }
            GameObject settingsRoot = new GameObject("SceneSetupController");
            SceneManager.MoveGameObjectToScene(settingsRoot, test);
            var settings = settingsRoot.AddComponent<SceneSetupController>();
            settings.presets = controller.presets;
            settings.cameraOffset = controller.cameraOffset;
            string controllerCopy = temporaryFolder + "/SceneSetupController.prefab";
            PrefabUtility.SaveAsPrefabAsset(settingsRoot, controllerCopy);
            Object.DestroyImmediate(settingsRoot);
            Object.DestroyImmediate(controller.gameObject);
            controller = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(controllerCopy), test).GetComponent<SceneSetupController>();
            // 被前一個控制器收納的回饋物件已隨它移除，重新建立後再保存。
            Configure(controller);
            Selection.activeGameObject = flower;
            SceneSetupCapture.SaveCurrentScene(controller);
            GameObject savedPlayer = Instantiate(controller.presets.Single(p => p.id == "player").prefab, other);
            Require(savedPlayer.GetComponent<PlayerMovement>().moveSpeed == 6.25f, "保存後重新拖入，玩家速度仍為修改後的 6.25");
            Require(savedPlayer.GetComponent<SceneSetupActor>().references.Count == 1, "再次保存只保留玩家的鏡頭引用規則");

            // 原始來源 Prefab 也能由 Tag＋來源種類加入設定，而非僅新生成的範本可用。
            GameObject originalSheep = Instantiate(sheepPreset.sourcePrefab, other);
            SceneSetupEditor.ApplyPreset(originalSheep, sheepPreset, false);
            Require(originalSheep.GetComponent<SceneSetupActor>().presetId == "enemy-sheep", "原始敵人預置體可辨識並套用保存設定");
            ValidateAssetReferences(controller.presets.Single(p => p.id == "player").prefab);

            GameObject emptyController = new GameObject("Script-only Controller");
            SceneManager.MoveGameObjectToScene(emptyController, latePlayerScene);
            var lateController = emptyController.AddComponent<SceneSetupController>();
            Configure(lateController);
            Require(lateController.presets.Count == 5, "只掛控制器腳本也只載入限定的五組基準設定");
            GameObject latePlayer = Instantiate(playerPreset.prefab, latePlayerScene);
            latePlayer.transform.position = new Vector3(40f, 0.15f, -25f);
            Configure(lateController);
            var lateCamera = SceneSetupController.ComponentsInScene<Camera>(latePlayerScene).Single();
            Require(Vector3.Distance(lateCamera.transform.position, latePlayer.transform.position + lateController.cameraOffset) < 0.001f,
                "先放控制器、後放玩家也會完成鏡頭定位");
            ValidateBindings(lateController);

            GameObject originalFlower = Instantiate(flowerPreset.sourcePrefab, other);
            SceneSetupEditor.ApplyPreset(originalFlower, flowerPreset, false);
            Require(originalFlower.GetComponent<Monster32StateHealth>().Body != null &&
                originalFlower.GetComponent<Monster32StateHealth>().Body.transform.IsChildOf(originalFlower.transform),
                "原始花怪預置體可補齊缺少的本體與本地引用");
            checks.Add("PASS：所有驗證完成。");
        }
        catch (Exception error)
        {
            checks.Add("FAIL：" + error);
            throw;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(test);
            EditorSceneManager.ClosePreviewScene(other);
            EditorSceneManager.ClosePreviewScene(baseline);
            EditorSceneManager.ClosePreviewScene(latePlayerScene);
            if (AssetDatabase.IsValidFolder(temporaryFolder)) AssetDatabase.DeleteAsset(temporaryFolder);
            Selection.activeObject = oldSelection;
            SceneSetupEditor.Busy = wasBusy;
            Directory.CreateDirectory("Temp/SceneSetup");
            File.WriteAllLines("Temp/SceneSetup/validation.txt", checks);
        }
        Debug.Log("【場景設定驗證】通過；完整報告：Temp/SceneSetup/validation.txt");
    }

    private static void Configure(SceneSetupController controller)
    {
        SceneSetupEditor.Busy = false;
        try { SceneSetupEditor.Configure(controller); }
        finally { SceneSetupEditor.Busy = true; }
    }

    private static GameObject Instantiate(GameObject prefab, Scene scene) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

    private static void Clear(SceneSetupActor.Reference reference)
    {
        var serialized = new SerializedObject(reference.owner);
        var property = serialized.FindProperty(reference.field);
        if (reference.arrayIndex >= 0) property = property.GetArrayElementAtIndex(reference.arrayIndex);
        property.objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Object GetReference(Component component, string field) => new SerializedObject(component).FindProperty(field).objectReferenceValue;

    private static void ValidateBindings(SceneSetupController controller)
    {
        var targets = controller.GetTargets();
        int count = 0;
        foreach (var actor in SceneSetupController.ComponentsInScene<SceneSetupActor>(controller.gameObject.scene))
        foreach (var reference in actor.references)
        {
            Object value = reference.Read();
            if (value == null || value != reference.Resolve(targets))
                throw new Exception($"連結遺失：{actor.name}/{reference.owner?.GetType().Name}.{reference.field}[{reference.arrayIndex}]");
            count++;
        }
        checks.Add($"引用檢查通過：{count} 個連結，包含未啟用的敵人。");
    }

    private static void ValidateAssetReferences(GameObject root)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) throw new Exception("預置體包含遺失的腳本：" + root.name);
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null &&
                    !EditorUtility.IsPersistent(property.objectReferenceValue))
                    throw new Exception("預置體保留了場景引用：" + root.name + "/" + property.propertyPath);
        }
    }

    private static int CompareSettings(GameObject original, GameObject prefab)
    {
        int count = 0;
        foreach (Component source in original.GetComponentsInChildren<Component>(true))
        {
            if (source == null || source is Transform || source is SceneSetupActor) continue;
            Transform node = SceneSetupActor.FindPath(prefab.transform, SceneSetupActor.GetPath(original.transform, source.transform));
            if (node == null) throw new Exception("保存缺少子物件：" + source.name);
            int index = Array.IndexOf(source.GetComponents(source.GetType()), source);
            Component destination = node.GetComponents(source.GetType())[index];
            var left = new SerializedObject(source);
            var right = new SerializedObject(destination);
            var property = left.GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = property.propertyType != SerializedPropertyType.ObjectReference;
                if (SceneSetupEditor.IsIdentity(property.propertyPath) || property.propertyType == SerializedPropertyType.Generic) continue;
                var other = right.FindProperty(property.propertyPath);
                if (other == null) throw new Exception("保存缺少欄位：" + property.propertyPath);
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                {
                    if (EditorUtility.IsPersistent(property.objectReferenceValue) && property.objectReferenceValue != other.objectReferenceValue)
                        throw new Exception("資產引用不同：" + source.GetType().Name + "/" + property.propertyPath);
                    continue;
                }
                if (!SerializedProperty.DataEquals(property, other))
                    throw new Exception($"基準設定不同：{original.name}/{source.GetType().Name}.{property.propertyPath}");
                count++;
            }
        }
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks.Add("通過：" + message);
    }
}
