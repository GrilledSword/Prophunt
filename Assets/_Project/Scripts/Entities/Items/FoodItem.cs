using Unity.Netcode;
using UnityEngine;

public class FoodItem : NetworkBehaviour
{
    [SerializeField] private float healAmount = 20f;

    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out PlayerNetworkController player))
        {
            if (player.IsOwner)
            {
                if (!player.isHunter.Value)
                {
                    player.SetNearbyFood(this);
                }
            }
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out PlayerNetworkController player))
        {
            if (player.IsOwner && !player.isHunter.Value)
            {
                player.SetNearbyFood(null);
            }
        }
    }
    public void Eat(HealthComponent health)
    {
        if (!IsServer) return;

        if (health != null)
        {
            health.ModifyHealth(healAmount);
        }

        if (IsSpawned)
        {
            GetComponent<NetworkObject>().Despawn();
        }
    }
}