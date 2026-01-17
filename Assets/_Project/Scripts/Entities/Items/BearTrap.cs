using Unity.Netcode;
using UnityEngine;

public class BearTrap : NetworkBehaviour
{
    private NetworkVariable<bool> isActivated = new NetworkVariable<bool>(false);

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (isActivated.Value) return;
        if (NetworkGameManager.Instance != null && !NetworkGameManager.Instance.areTrapsActive.Value)
        {
            return;
        }

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
        Debug.Log("CSATT!");
    }
}