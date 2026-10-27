using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmallMonsterSetup
{
    private const string Art = "Assets/animation/moster1/small/";
    private const string Prefab = Art + "moster1 small.prefab";

    [MenuItem("Koto/分裂小球/設定預置體與目前場景")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try { Configure(root, false); PrefabUtility.SaveAsPrefabAsset(root, Prefab); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        int count = 0;
        foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
        {
            var obj = animator.gameObject;
            if (!obj.scene.IsValid() || !obj.scene.isLoaded || EditorSceneManager.IsPreviewScene(obj.scene)) continue;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obj) != Prefab &&
                obj.name.Trim() != "moster1 small" && obj.name.Trim() != "moster1 smell") continue;
            Configure(obj, true);
            EditorSceneManager.MarkSceneDirty(obj.scene);
            count++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"分裂小球預置體已設定，場景內已設定 {count} 隻；請保存場景。");
        Directory.CreateDirectory("Temp/SmallMonster");
        File.WriteAllText("Temp/SmallMonster/setup.txt", $"PASS prefab; scene instances={count}");
    }

    private static T Ensure<T>(GameObject obj, bool undo) where T : Component =>
        obj.GetComponent<T>() ?? (undo ? Undo.AddComponent<T>(obj) : obj.AddComponent<T>());

    private static void Configure(GameObject obj, bool undo)
    {
        if (undo) Undo.RecordObject(obj, "設定分裂小球");
        obj.tag = "Enemy";
        var animation = Ensure<SmallMonsterAnimation>(obj, undo);
        Ensure<SmallMonsterDetection>(obj, undo);
        Ensure<EnemyAreaMovement>(obj, undo);
        var combat = Ensure<SmallMonsterCombat>(obj, undo);
        var settings = new SerializedObject(animation);
        settings.FindProperty("animator").objectReferenceValue = obj.GetComponent<Animator>();
        settings.FindProperty("walk").objectReferenceValue = Clip("moster1 small walk.anim");
        settings.FindProperty("summon").objectReferenceValue = Clip("moster1 small call.anim");
        settings.FindProperty("attack").objectReferenceValue = Clip("moster1  small attack.anim");
        settings.FindProperty("death").objectReferenceValue = Clip("moster1 small die.anim");
        settings.ApplyModifiedProperties();
        var point = obj.transform.Find("AttackPoint");
        if (point == null)
        {
            var child = new GameObject("AttackPoint");
            if (undo) Undo.RegisterCreatedObjectUndo(child, "新增攻擊點");
            child.transform.SetParent(obj.transform, false);
            point = child.transform;
        }
        settings = new SerializedObject(combat);
        if (settings.FindProperty("attackPoint").objectReferenceValue == null)
            settings.FindProperty("attackPoint").objectReferenceValue = point;
        settings.ApplyModifiedProperties();
        if (undo && PrefabUtility.IsPartOfPrefabInstance(obj))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(combat);
        }
    }
    private static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Art + name);

}
