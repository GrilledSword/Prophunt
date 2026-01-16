using Unity.Netcode;
using UnityEngine;

public class SafeZone : NetworkBehaviour
{
    [SerializeField] private GameObject visualMarker; // Pl. egy fényoszlop, hogy lássa hova kell futni

    // Csak akkor hívjuk, ha aktiválódik a menekülés
    public void SetActive(bool isActive)
    {
        if (visualMarker != null) visualMarker.SetActive(isActive);
        GetComponent<Collider>().enabled = isActive;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // Csak akkor mûködik, ha Pánik van!
        if (!NetworkGameManager.Instance.IsHunterPanic()) return;

        // Megkeressük a komponenst (Parentben is, ha a lábát dugja be)
        var player = other.GetComponentInParent<PlayerNetworkController>();

        if (player != null && player.isHunter.Value)
        {
            Debug.Log("VADÁSZ BEÉRT A HÁZBA! GYÕZELEM!");
            NetworkGameManager.Instance.EndGameServerRpc(true); // Hunter Won
        }
    }
}