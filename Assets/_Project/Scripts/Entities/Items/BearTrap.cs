using Unity.Netcode;
using UnityEngine;

public class BearTrap : NetworkBehaviour
{
    private NetworkVariable<bool> isActivated = new NetworkVariable<bool>(false);

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (isActivated.Value) return;

        Debug.Log($"[BearTrap] Valami ráment: {other.name}"); // DEBUG

        // Szintén a szülõt keressük, hátha a "Foot" collidert találtuk el
        var victimController = other.GetComponentInParent<PlayerNetworkController>();

        if (victimController != null)
        {
            isActivated.Value = true;
            victimController.SetTrappedClientRpc(true);
            CloseTrapClientRpc();
        }
    }

    [ClientRpc]
    private void CloseTrapClientRpc()
    {
        // Itt játszd le a csapda animációt / hangot
        Debug.Log("CSATT!");
    }
}