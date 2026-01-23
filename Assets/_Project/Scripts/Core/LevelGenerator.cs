using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class LevelGenerator : NetworkBehaviour
{
    public static LevelGenerator Instance { get; private set; }

    [Header("Spawn Ter�let")]
    [SerializeField] private BoxCollider spawnArea;
    [SerializeField] private LayerMask groundLayer;

    [Header("Kaja Be�ll�t�sok")]
    [SerializeField] private GameObject foodPrefab;
    [SerializeField] private int foodCount = 10;

    [Header("Vesz�ly Be�ll�t�sok")]
    [SerializeField] private GameObject landminePrefab;
    [SerializeField] private int landmineCount = 5;

    [SerializeField] private GameObject bearTrapPrefab;
    [SerializeField] private int bearTrapCount = 5;

    [Header("NPC Be�ll�t�sok")]
    [SerializeField] private GameObject deerNpcPrefab;
    [SerializeField] private int npcCount = 25;

    private List<NetworkObject> spawnedObjects = new List<NetworkObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }
    public void GenerateLevel(NetworkGameManager.RoundType roundType)
    {
        if (!IsServer) return;
        // [REMOVED] ClearLevel() már a GameLoopManager-ben meghívódik - ne duplázódjon!
        
        SpawnObjects(foodPrefab, foodCount);
        if (deerNpcPrefab != null)
        {
            SpawnObjects(deerNpcPrefab, npcCount);
        }

        switch (roundType)
        {
            case NetworkGameManager.RoundType.Normal:
                break;
            case NetworkGameManager.RoundType.Mines:
                SpawnObjects(landminePrefab, landmineCount);
                break;
            case NetworkGameManager.RoundType.Traps:
                SpawnObjects(bearTrapPrefab, bearTrapCount);
                break;
        }
    }
    public void ClearLevel()
    {
        if (!IsServer) return;

        int despawnedCount = 0;

        // 1. Összes tracked objektum despawnolása
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
            {
                try
                {
                    if (obj.IsSpawned)
                    {
                        obj.Despawn(false);
                        despawnedCount++;
                        Debug.Log($"[LevelGenerator] Despawned: {obj.gameObject.name}");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[LevelGenerator] Error despawning {obj.gameObject.name}: {ex.Message}");
                }
            }
        }
        spawnedObjects.Clear();

        // 2. Biztonsági takarítás: Keressen meg minden runtime-spawned objektumot
        // Keresünk: Food, Landmine, BearTrap, DeerNPC komponenseket
        var allFood = FindObjectsByType<FoodItem>(FindObjectsSortMode.None);
        foreach (var food in allFood)
        {
            if (food == null) continue;
            var netObj = food.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(false);
                despawnedCount++;
                Debug.Log($"[LevelGenerator] Extra despawned: {food.gameObject.name}");
            }
        }

        var allMines = FindObjectsByType<Landmine>(FindObjectsSortMode.None);
        foreach (var mine in allMines)
        {
            if (mine == null) continue;
            var netObj = mine.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(false);
                despawnedCount++;
                Debug.Log($"[LevelGenerator] Extra despawned: {mine.gameObject.name}");
            }
        }

        var allTraps = FindObjectsByType<BearTrap>(FindObjectsSortMode.None);
        foreach (var trap in allTraps)
        {
            if (trap == null) continue;
            var netObj = trap.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(false);
                despawnedCount++;
                Debug.Log($"[LevelGenerator] Extra despawned: {trap.gameObject.name}");
            }
        }

        // Szarvasok (csak NPC-k, nem Player!)
        var allDeerNpc = FindObjectsByType<DeerAIController>(FindObjectsSortMode.None);
        foreach (var deer in allDeerNpc)
        {
            if (deer == null) continue;
            var netObj = deer.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(false);
                despawnedCount++;
                Debug.Log($"[LevelGenerator] Extra despawned: {deer.gameObject.name}");
            }
        }

        Debug.Log($"[LevelGenerator] ✅ Összes objektum megtisztítva! Despawned: {despawnedCount} darab");
    }

    private void SpawnObjects(GameObject prefab, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 randomPos = GetRandomPosition();
            if (randomPos == Vector3.zero) continue;

            GameObject obj = Instantiate(prefab, randomPos, Quaternion.identity);
            NetworkObject netObj = obj.GetComponent<NetworkObject>();

            if (netObj != null)
            {
                netObj.Spawn();
                spawnedObjects.Add(netObj);
            }
        }
    }

    private Vector3 GetRandomPosition()
    {
        if (spawnArea == null) return Vector3.zero;

        Bounds bounds = spawnArea.bounds;
        for (int i = 0; i < 10; i++)
        {
            float randomX = Random.Range(bounds.min.x, bounds.max.x);
            float randomZ = Random.Range(bounds.min.z, bounds.max.z);
            Vector3 rayStart = new Vector3(randomX, bounds.max.y, randomZ);

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, bounds.size.y + 10f, groundLayer))
            {
                return hit.point + Vector3.up * 0.1f;
            }
        }
        return Vector3.zero;
    }
}