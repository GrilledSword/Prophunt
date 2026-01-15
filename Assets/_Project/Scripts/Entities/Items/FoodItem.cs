using Unity.Netcode;
using UnityEngine;

public class FoodItem : NetworkBehaviour
{
    [SerializeField] private float healAmount = 20f;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // Csak Játékos veheti fel
        if (other.TryGetComponent(out PlayerNetworkController player))
        {
            // Csak SZARVAS veheti fel
            if (!player.isHunter.Value)
            {
                // Gyógyítás
                if (other.TryGetComponent(out HealthComponent health))
                {
                    health.ModifyHealth(healAmount);

                    // Kaja eltûnik
                    GetComponent<NetworkObject>().Despawn();
                }
            }
        }
    }
}