using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>預置體內的跨場景連結記錄；角色數值仍直接保存在原本的各個元件。</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-31900)]
[AddComponentMenu("Koto/場景預置體連結")]
public sealed class SceneSetupActor : MonoBehaviour
{
    [Serializable]
    public sealed class Reference
    {
        public MonoBehaviour owner;
        public string field;
        public int arrayIndex = -1;
        public string targetId;
        public string targetPath;
        public string componentType;
        public int componentIndex;

        public FieldInfo GetField()
        {
            if (owner == null) return null;
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo info = type.GetField(field, BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (info != null) return info;
            }
            return null;
        }

        public UnityEngine.Object Read()
        {
            FieldInfo info = GetField();
            if (info == null) return null;
            object value = info.GetValue(owner);
            if (arrayIndex < 0) return value as UnityEngine.Object;
            return value is IList list && arrayIndex < list.Count ? list[arrayIndex] as UnityEngine.Object : null;
        }

        public bool Write(UnityEngine.Object value)
        {
            FieldInfo info = GetField();
            if (info == null || value == null || Read() == value) return false;
            if (arrayIndex < 0)
            {
                if (!info.FieldType.IsInstanceOfType(value)) return false;
                info.SetValue(owner, value);
                return true;
            }
            if (!(info.GetValue(owner) is IList list) || arrayIndex >= list.Count) return false;
            list[arrayIndex] = value;
            return true;
        }

        public UnityEngine.Object Resolve(Dictionary<string, GameObject> targets)
        {
            if (!targets.TryGetValue(targetId, out GameObject root) || root == null) return null;
            Transform node = FindPath(root.transform, targetPath);
            if (node == null) return null;
            if (string.IsNullOrEmpty(componentType)) return node.gameObject;
            Type type = Type.GetType(componentType);
            if (type == null) return null;
            Component[] components = node.GetComponents(type);
            return componentIndex < components.Length ? components[componentIndex] : null;
        }
    }

    [HideInInspector] public string presetId;
    [HideInInspector] public bool cameraPositionInitialized;
    [HideInInspector] public List<Reference> references = new List<Reference>();

    private void Awake()
    {
        SceneSetupController.FindInScene(gameObject.scene)?.BindSceneReferences();
    }

    public void Bind(Dictionary<string, GameObject> targets)
    {
        foreach (Reference reference in references)
            if (!HasSceneReference(reference.Read(), gameObject.scene))
                reference.Write(reference.Resolve(targets));
    }

    public static bool HasSceneReference(UnityEngine.Object value, UnityEngine.SceneManagement.Scene scene)
    {
        if (value == null) return false;
        GameObject go = value is Component component ? component.gameObject : value as GameObject;
        return go != null && go.scene == scene;
    }

    // 每一段記錄「名稱 + 同名子物件序號」，不受其他兄弟物件新增、重新排序影響。
    public static string GetPath(Transform root, Transform node)
    {
        if (root == node) return string.Empty;
        if (node == null || !node.IsChildOf(root)) throw new ArgumentException("物件不在指定根物件內。");
        var segments = new List<string>();
        while (node != root)
        {
            int occurrence = 0;
            for (int i = 0; i < node.GetSiblingIndex(); i++)
                if (node.parent.GetChild(i).name == node.name) occurrence++;
            segments.Add(Uri.EscapeDataString(node.name) + ":" + occurrence);
            node = node.parent;
        }
        segments.Reverse();
        return string.Join("/", segments);
    }

    public static Transform FindPath(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path)) return root;
        Transform node = root;
        foreach (string segment in path.Split('/'))
        {
            int split = segment.LastIndexOf(':');
            if (split < 0 || !int.TryParse(segment.Substring(split + 1), out int occurrence)) return null;
            string name = Uri.UnescapeDataString(segment.Substring(0, split));
            Transform found = null;
            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child.name == name && occurrence-- == 0) { found = child; break; }
            }
            if (found == null) return null;
            node = found;
        }
        return node;
    }
}
