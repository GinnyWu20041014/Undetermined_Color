using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 依設定的間隔自動生成敵人。
/// 將此元件掛在場景空物件上，並在 Inspector 放入敵人 Prefab。
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("敵人 Prefab")]
    [Tooltip("可加入多個敵人 Prefab；每次會隨機選擇一個生成。")]
    [SerializeField] private GameObject[] enemyPrefabs = System.Array.Empty<GameObject>();

    [Header("生成設定")]
    [Tooltip("可加入多個生成點；未指定時會在此物件的位置生成。")]
    [SerializeField] private Transform[] spawnPoints = System.Array.Empty<Transform>();
    [SerializeField, Min(0.01f)] private float spawnInterval = 3f;
    [SerializeField, Min(1)] private int maxAliveEnemies = 5;
    [SerializeField] private bool spawnImmediately = true;

    private readonly List<GameObject> spawnedEnemies = new List<GameObject>();
    private float nextSpawnTime;

    private void Start()
    {
        nextSpawnTime = spawnImmediately ? Time.time : Time.time + spawnInterval;
    }

    private void Update()
    {
        RemoveDefeatedEnemies();

        if (Time.time >= nextSpawnTime && spawnedEnemies.Count < maxAliveEnemies)
        {
            SpawnEnemy();
            nextSpawnTime = Time.time + spawnInterval;
        }
    }

    /// <summary>可由事件或其他腳本手動呼叫，立即生成一名敵人。</summary>
    public void SpawnEnemy()
    {
        GameObject enemyPrefab = GetRandomEnemyPrefab();
        if (enemyPrefab == null)
        {
            Debug.LogWarning("【敵人生成器】請先在 Inspector 的「敵人 Prefab」加入至少一個 Prefab。", this);
            return;
        }

        Transform spawnPoint = GetRandomSpawnPoint();
        GameObject enemy = Instantiate(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
        spawnedEnemies.Add(enemy);
    }

    private GameObject GetRandomEnemyPrefab()
    {
        List<GameObject> validPrefabs = new List<GameObject>();
        foreach (GameObject enemyPrefab in enemyPrefabs)
        {
            if (enemyPrefab != null)
            {
                validPrefabs.Add(enemyPrefab);
            }
        }

        return validPrefabs.Count == 0 ? null : validPrefabs[Random.Range(0, validPrefabs.Count)];
    }

    private Transform GetRandomSpawnPoint()
    {
        if (spawnPoints.Length == 0)
        {
            return transform;
        }

        List<Transform> validPoints = new List<Transform>();
        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint != null)
            {
                validPoints.Add(spawnPoint);
            }
        }

        return validPoints.Count == 0 ? transform : validPoints[Random.Range(0, validPoints.Count)];
    }

    private void RemoveDefeatedEnemies()
    {
        spawnedEnemies.RemoveAll(enemy => enemy == null);
    }
}
