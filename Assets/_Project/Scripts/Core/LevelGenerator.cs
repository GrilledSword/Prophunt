using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class LevelGenerator : NetworkBehaviour
{
    public static LevelGenerator Instance { get; private set; }

    [Header("Spawn Terület")]
    [SerializeField] private BoxCollider spawnArea;
    [SerializeField] private LayerMask groundLayer;

    [Header("Kaja Beállítások")]
    [SerializeField] private GameObject foodPrefab;
    [SerializeField] private int foodCount = 10;

    [Header("Veszély Beállítások")]
    [SerializeField] private GameObject landminePrefab;
    [SerializeField] private int landmineCount = 5;

    [SerializeField] private GameObject bearTrapPrefab;
    [SerializeField] private int bearTrapCount = 5;

    [Header("NPC Beállítások")]
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
        ClearLevel();
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
        foreach (var obj in spawnedObjects)
        {
            if (obj != null && obj.IsSpawned)
            {
                obj.Despawn();
            }
        }
        spawnedObjects.Clear();

        Landmine[] mines = FindObjectsOfType<Landmine>();
        foreach (var mine in mines)
        {
            if (mine.GetComponent<NetworkObject>().IsSpawned)
                mine.GetComponent<NetworkObject>().Despawn();
        }

        BearTrap[] traps = FindObjectsOfType<BearTrap>();
        foreach (var trap in traps)
        {
            if (trap.GetComponent<NetworkObject>().IsSpawned)
                trap.GetComponent<NetworkObject>().Despawn();
        }

        FoodItem[] foods = FindObjectsOfType<FoodItem>();
        foreach (var food in foods)
        {
            if (food.GetComponent<NetworkObject>().IsSpawned)
                food.GetComponent<NetworkObject>().Despawn();
        }

        DeerAIController[] npcs = FindObjectsOfType<DeerAIController>();
        foreach (var npc in npcs)
        {
            if (npc.GetComponent<NetworkObject>().IsSpawned)
                npc.GetComponent<NetworkObject>().Despawn();
        }
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