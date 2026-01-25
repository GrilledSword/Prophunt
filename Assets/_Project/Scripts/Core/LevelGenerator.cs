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
    public override void OnNetworkSpawn()
    {
        // [ÚJ] Amint a szerveren létrejön ez a szkript (pálya betöltéskor),
        // azonnal takarítunk, hogy ne maradjon szemét az előző körből.
        if (IsServer)
        {
            Debug.Log("[LevelGenerator] Scene loaded. Cleaning up potential leftovers...");
            ClearPreviousRoundObjects();
        }
    }
    public void GenerateLevel(NetworkGameManager.RoundType roundType)
    {
        if (!IsServer) return;

        // Biztonsági takarítás generálás előtt is
        ClearPreviousRoundObjects();

        Debug.Log($"[LevelGenerator] 🧹 Clearing old level before generating RoundType: {roundType}");

        // Mindig spawnol: Food + NPC-k
        SpawnObjects(foodPrefab, foodCount);
        if (deerNpcPrefab != null)
        {
            SpawnObjects(deerNpcPrefab, npcCount);
        }

        // Round type specifikus objektumok
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

        Debug.Log($"[LevelGenerator] ✅ Level generated with RoundType: {roundType}");
    }
    private void ClearPreviousRoundObjects()
    {
        int despawnedCount = 0;

        // 1. Ismert objektumok törlése (ha van a listában)
        // Újratöltésnél ez a lista üres, de meccs közbeni újragenerálásnál hasznos.
        foreach (var obj in spawnedObjects)
        {
            if (obj != null && obj.IsSpawned)
            {
                try
                {
                    obj.Despawn(false);
                    despawnedCount++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[LevelGenerator] Error despawning known object: {ex.Message}");
                }
            }
        }
        spawnedObjects.Clear();

        // 2. [JAVÍTOTT] Biztonsági takarítás: Keressen meg MINDEN runtime-spawned objektumot.
        // Most már az INAKTÍV objektumokat is keressük (FindObjectsInactive.Include)!
        
        void DespawnList<T>() where T : Component
        {
            var foundObjects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var item in foundObjects)
            {
                if (item == null) continue;
                
                // Csak akkor töröljük, ha van rajta NetworkObject
                var netObj = item.GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(false);
                    despawnedCount++;
                    Debug.Log($"[LevelGenerator] Ghost despawned: {item.gameObject.name}");
                }
                else if (netObj == null) 
                {
                    // Ha nincs NetworkObject, de ott van (pl. kliens oldali szellem), simán Destroy
                    Destroy(item.gameObject);
                }
            }
        }

        DespawnList<FoodItem>();
        DespawnList<Landmine>();
        DespawnList<BearTrap>();
        DespawnList<DeerAIController>();

        Debug.Log($"[LevelGenerator] ✅ Clean sweep complete! Total removed: {despawnedCount}");
    }
    private void SpawnObjects(GameObject prefab, int count)
    {
        if (prefab == null) return;

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