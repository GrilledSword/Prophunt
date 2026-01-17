using Unity.Netcode;
using UnityEngine;

public class Landmine : NetworkBehaviour
{
    [SerializeField] private GameObject explosionEffect;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (NetworkGameManager.Instance != null && !NetworkGameManager.Instance.areTrapsActive.Value)
        {
            return;
        }
        var victimHealth = other.GetComponentInParent<HealthComponent>();

        if (victimHealth != null)
        {
            victimHealth.TakeHit(9999, true);
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