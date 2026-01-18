using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ArrowProjectile : NetworkBehaviour
{
    [Header("Játék Logika")]
    [SerializeField] private float damage = 9999f; // Azonnali halál
    [SerializeField] private float cost = 25f;     // Büntetés (NPC ölés)
    [SerializeField] private float reward = 25f;   // Jutalom (Játékos ölés)

    [Header("Beállítások")]
    [SerializeField] private float lifeTime = 10f;
    [SerializeField] private float destroyTimeAfterHit = 5f;

    [Header("Visuals")]
    [SerializeField] private TrailRenderer trailRenderer;

    private Rigidbody rb;
    private bool hasHit = false;
    private ulong shooterObjectId; // A lövő hálózati azonosítója

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();
        // FONTOS: Gyors mozgásnál kötelező a ContinuousDynamic
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (IsServer) Destroy(gameObject, lifeTime);
    }

    public void Initialize(ulong shooterObjId)
    {
        shooterObjectId = shooterObjId;
    }

    private void FixedUpdate()
    {
        // Forgatás a repülés irányába
        if (!hasHit && rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.1f)
        {
            transform.rotation = Quaternion.LookRotation(rb.linearVelocity);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || hasHit) return;

        // Saját magunkat (Lövőt) ne találjuk el
        NetworkObject hitNetObj = other.GetComponentInParent<NetworkObject>();
        if (hitNetObj != null && hitNetObj.NetworkObjectId == shooterObjectId) return;

        // Triggerek szűrése (kivéve CharacterController, mert az fontos)
        if (other.isTrigger && !other.GetComponent<CharacterController>())
        {
            if (other.name.ToLower().Contains("aggro") || other.name.ToLower().Contains("zone")) return;
        }

        hasHit = true;
        bool hitLivingTarget = false;

        Debug.Log($"[Arrow] TALÁLAT: {other.name} | Szülő: {other.transform.root.name}");

        // --- TALÁLAT LOGIKA ---

        // 1. Játékos keresése (HealthComponent)
        var targetHealth = other.GetComponentInParent<HealthComponent>();
        if (targetHealth != null)
        {
            // Ellenséges játékos találat
            targetHealth.TakeHit(damage);
            hitLivingTarget = true;
            Debug.Log(">>> JÁTÉKOS LELŐVE! +HP a Vadásznak.");

            // JUTALOM: Adunk életet a vadásznak
            ModifyShooterHealth(reward);
        }

        // 2. NPC keresése (DeerAIController) - Ha nem Játékos volt
        if (!hitLivingTarget)
        {
            var npcController = other.GetComponentInParent<DeerAIController>();
            if (npcController != null)
            {
                hitLivingTarget = true;
                Debug.Log(">>> NPC ELTALÁLVA -> Despawn és -HP a Vadásznak!");

                // BÜNTETÉS: Levonunk életet a vadásztól
                ModifyShooterHealth(-cost);

                // NPC eltüntetése
                NetworkObject npcNetObject = other.GetComponentInParent<NetworkObject>();
                if (npcNetObject != null && npcNetObject.IsSpawned)
                {
                    npcNetObject.Despawn();
                }
            }
        }

        // --- NYÍL SORSA ---

        if (hitLivingTarget)
        {
            // Ha élőlényt találtunk, a nyíl tűnjön el
            GetComponent<NetworkObject>().Despawn();
        }
        else
        {
            // Fal találat - Álljon meg
            StopArrow();
        }
    }

    // [ADDED] Segédfüggvény a lövő életének módosítására
    private void ModifyShooterHealth(float amount)
    {
        // Megkeressük a lövő objektumot az ID alapján a SpawnManager-ben
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(shooterObjectId, out NetworkObject shooterObj))
        {
            var shooterHealth = shooterObj.GetComponent<HealthComponent>();
            if (shooterHealth != null)
            {
                shooterHealth.ModifyHealth(amount);
                Debug.Log($"[Arrow] Vadász élete módosítva: {amount}");
            }
        }
        else
        {
            Debug.LogWarning("[Arrow] Nem található a lövő játékos a szerveren (lehet, hogy kilépett).");
        }
    }

    private void StopArrow()
    {
        // Unity 6 kompatibilis megállítás
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
        }

        var colliders = GetComponentsInChildren<Collider>();
        foreach (var c in colliders) c.enabled = false;

        if (trailRenderer) trailRenderer.enabled = false;

        Invoke(nameof(DespawnArrow), destroyTimeAfterHit);
        SetKinematicClientRpc();
    }

    [ClientRpc]
    private void SetKinematicClientRpc()
    {
        if (TryGetComponent(out Rigidbody r))
        {
            r.linearVelocity = Vector3.zero;
            r.collisionDetectionMode = CollisionDetectionMode.Discrete;
            r.isKinematic = true;
        }
        var colliders = GetComponentsInChildren<Collider>();
        foreach (var c in colliders) c.enabled = false;
        if (trailRenderer) trailRenderer.enabled = false;
    }

    private void DespawnArrow()
    {
        if (IsServer && GetComponent<NetworkObject>().IsSpawned)
        {
            GetComponent<NetworkObject>().Despawn();
        }
    }
}