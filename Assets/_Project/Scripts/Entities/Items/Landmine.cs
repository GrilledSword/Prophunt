using Unity.Netcode;
using UnityEngine;

public class Landmine : NetworkBehaviour
{
    [SerializeField] private GameObject explosionEffect; // Particle System prefab

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        Debug.Log($"[Landmine] Valami ráment: {other.name}"); // DEBUG

        // Elõször a gyökérobjektumon keressük a komponenst
        var victimHealth = other.GetComponentInParent<HealthComponent>();

        if (victimHealth != null)
        {
            // BUMM!
            victimHealth.TakeHit(9999, true); // True = InstaKill
            TriggerExplosionClientRpc();
            GetComponent<NetworkObject>().Despawn();
        }
    }

    [ClientRpc]
    private void TriggerExplosionClientRpc()
    {
        if (explosionEffect != null)
        {
            Instantiate(explosionEffect, transform.position, Quaternion.identity);
        }
    }
}