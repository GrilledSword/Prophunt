using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ArrowProjectile : NetworkBehaviour
{
    [Header("Beállítások")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float lifeTime = 10f;
    [SerializeField] private float destroyTimeAfterHit = 5f;

    [Header("Visuals")]
    [SerializeField] private TrailRenderer trailRenderer;

    private Rigidbody rb;
    private bool hasHit = false;
    private ulong shooterClientId;

    public override void OnNetworkSpawn()
    {
        rb = GetComponent<Rigidbody>();
        if (IsServer)
        {
            Destroy(gameObject, lifeTime);
        }
    }

    public void Initialize(ulong shooterId)
    {
        shooterClientId = shooterId;
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

        // [FONTOS] Ne ütközzünk más Triggerekkel! 
        // (Pl. az NPC "Aggro Radius" gömbjével, ami láthatatlan)
        if (other.isTrigger) return;

        // Saját magunkat ne találjuk el
        NetworkObject hitNetObj = other.GetComponentInParent<NetworkObject>();
        if (hitNetObj != null && hitNetObj.OwnerClientId == shooterClientId) return;

        hasHit = true;
        bool hitLivingTarget = false;

        // --- DEBUG: Lássuk pontosan mit találtunk el ---
        Debug.Log($"🏹 NYÍL TALÁLAT! Eltalált Collider: '{other.name}' | Szülő: '{other.transform.root.name}'");

        // 1. Játékos keresése
        var targetHealth = other.GetComponentInParent<HealthComponent>();
        if (targetHealth != null)
        {
            targetHealth.TakeHit(9999);
            hitLivingTarget = true;
            Debug.Log(">>> JÁTÉKOS TALÁLAT (HealthComponent megvan)!");
        }

        // 2. NPC keresése (Ha nem játékos volt)
        if (!hitLivingTarget)
        {
            var npcController = other.GetComponentInParent<DeerAIController>();
            if (npcController != null)
            {
                hitLivingTarget = true;
                Debug.Log(">>> NPC SZARVAS TALÁLAT (DeerAIController megvan)!");

                // Ha van az NPC-n HealthComponent, azt is sebezzük
                var npcHealth = other.GetComponentInParent<HealthComponent>();
                if (npcHealth) npcHealth.TakeHit(9999);
            }
        }

        // --- REAKCIÓ ---
        if (hitLivingTarget)
        {
            Debug.Log("-> Élőlény találat: Törlés");
            GetComponent<NetworkObject>().Despawn();
        }
        else
        {
            Debug.Log("-> Fal/Tárgy találat: Megállás");
            StopArrow();
        }
    }

    private void StopArrow()
    {
        // [JAVÍTVA] Unity 6 kompatibilis sorrend!
        if (rb != null)
        {
            // 1. Először nullázzuk a sebességet
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // 2. Csak utána fagyasztjuk le
            rb.isKinematic = true;
        }

        // Triggerek kikapcsolása
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