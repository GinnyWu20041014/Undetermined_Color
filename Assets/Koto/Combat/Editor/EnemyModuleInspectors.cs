using UnityEditor;
using UnityEngine;

/// <summary>使用 IMGUI 繪製清單，避免舊版 Unity UI Toolkit Inspector 綁定失效。</summary>
public abstract class EnemyModuleInspector : Editor
{
    public override void OnInspectorGUI()
    {
        if (target == null) return;
        DrawDefaultInspector();
    }
}

[CustomEditor(typeof(EnemyAreaMovement)), CanEditMultipleObjects]
public sealed class EnemyAreaMovementEditor : EnemyModuleInspector { }

[CustomEditor(typeof(EnemyStateHealth)), CanEditMultipleObjects]
public sealed class EnemyStateHealthEditor : EnemyModuleInspector { }

[CustomEditor(typeof(EnemyAnimationController)), CanEditMultipleObjects]
public sealed class EnemyAnimationControllerEditor : EnemyModuleInspector { }

[CustomEditor(typeof(EnemyPlayerAttackController)), CanEditMultipleObjects]
public sealed class EnemyPlayerAttackControllerEditor : EnemyModuleInspector { }

[CustomEditor(typeof(EnemyController)), CanEditMultipleObjects]
public sealed class EnemyControllerMigrationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (target == null) return;
        EditorGUILayout.HelpBox(
            "這是舊版設定資料。按下移轉後，會建立四個可獨立掛載的元件並移除舊 EnemyController。\n" +
            "已存在的獨立元件會保留目前設定；新建立的元件會帶入舊設定。操作可使用 Undo 還原。",
            MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("移轉為四個獨立腳本（保留設定）"))
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("移轉敵人為四個獨立腳本");
                foreach (Object selected in targets)
                {
                    var legacy = selected as EnemyController;
                    if (legacy == null) continue;
                    if (EditorUtility.IsPersistent(legacy))
                    {
                        Debug.LogWarning("【敵人移轉】請先開啟 Prefab 編輯模式，再移轉此敵人。", legacy);
                        continue;
                    }
                    Component[] modules = legacy.MigrateToIndependentScripts(type =>
                    {
                        Component added = Undo.AddComponent(legacy.gameObject, type);
                        Undo.RegisterCompleteObjectUndo(added, "複製敵人設定");
                        return added;
                    });
                    foreach (Component module in modules)
                    {
                        EditorUtility.SetDirty(module);
                        if (PrefabUtility.IsPartOfPrefabInstance(module))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(module);
                    }
                    Undo.DestroyObjectImmediate(legacy);
                }
                Undo.CollapseUndoOperations(group);
                GUIUtility.ExitGUI();
            }
        }
    }
}
