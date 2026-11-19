using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>玩家碰觸此區域後，載入指定場景。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class SceneExitTrigger : MonoBehaviour
{
#if UNITY_EDITOR
    [InspectorName("目標場景")]
    [SerializeField] private SceneAsset nextScene = null;

    private void OnValidate()
    {
        nextScenePath = nextScene != null ? AssetDatabase.GetAssetPath(nextScene) : string.Empty;
    }
#endif

    [SerializeField, HideInInspector] private string nextScenePath;
    private static string pendingScenePath;

    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(pendingScenePath)) return;

        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null && other.attachedRigidbody != null)
            player = other.attachedRigidbody.GetComponentInChildren<PlayerMovement>();
        if (player == null) return;

        PlayerHealth health = player.GetComponentInChildren<PlayerHealth>();
        if (health == null) health = player.GetComponentInParent<PlayerHealth>();
        if (health != null && health.IsDead) return;

        if (string.IsNullOrEmpty(nextScenePath))
        {
            Debug.LogWarning("【切換場景】請先指定目標場景。", this);
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(nextScenePath))
        {
            Debug.LogError($"【切換場景】請將 {nextScenePath} 加入並啟用於 Build Profiles 的 Scene List。", this);
            return;
        }

        pendingScenePath = nextScenePath;
        try
        {
            SceneManager.LoadScene(nextScenePath, LoadSceneMode.Single);
        }
        catch
        {
            pendingScenePath = null;
            throw;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneLoaded()
    {
        pendingScenePath = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (string.IsNullOrEmpty(pendingScenePath) || scene.path != pendingScenePath) return;
        pendingScenePath = null;

        Transform spawnPoint = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (SceneSpawnPoint point in root.GetComponentsInChildren<SceneSpawnPoint>())
            {
                if (!point.isActiveAndEnabled) continue;
                spawnPoint = point.transform;
                break;
            }
            if (spawnPoint != null) break;
        }

        // sceneLoaded 發生在 Awake 之後、Start 之前，先定位再開始玩家操作。
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (PlayerHealth player in root.GetComponentsInChildren<PlayerHealth>())
                player.ApplySceneEntrySpawn(spawnPoint);
    }
}
