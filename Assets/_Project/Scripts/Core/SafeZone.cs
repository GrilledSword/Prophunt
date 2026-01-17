using Unity.Netcode;
using UnityEngine;

public class SafeZone : NetworkBehaviour
{
    [SerializeField] private GameObject visualMarker;

    public void SetActive(bool isActive)
    {
        if (visualMarker != null) visualMarker.SetActive(isActive);
        GetComponent<Collider>().enabled = isActive;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!NetworkGameManager.Instance.IsHunterPanic()) return;

        var player = other.GetComponentInParent<PlayerNetworkController>();

        if (player != null && player.isHunter.Value)
        {
            Debug.Log("VADÁSZ BEÉRT A HÁZBA! GYÕZELEM!");
            NetworkGameManager.Instance.EndGameServerRpc(true);
        }
    }
}