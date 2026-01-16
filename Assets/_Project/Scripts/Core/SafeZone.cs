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

        // Ha a "Megõrült Vadász" lép be
        if (other.TryGetComponent(out PlayerNetworkController player))
        {
            if (player.isHunter.Value && NetworkGameManager.Instance.IsHunterInPanic())
            {
                // A Vadász túlélte!
                NetworkGameManager.Instance.EndGameServerRpc(true); // True = Hunter Won (Survived)
            }
        }
    }
}