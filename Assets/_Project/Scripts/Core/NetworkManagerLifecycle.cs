using UnityEngine;
using Unity.Netcode;

public class NetworkManagerLifecycle : MonoBehaviour
{
    private void Awake()
    {
        // Ha már létezik egy NetworkManager (pl. visszajöttünk a játékból és nem törlõdött),
        // akkor MI feleslegesek vagyunk. Vagy a régit töröljük.
        // A legtisztább logika Menü betöltéskor:

        if (NetworkManager.Singleton != null && NetworkManager.Singleton != GetComponent<NetworkManager>())
        {
            // Már van egy Fõnök. Mivel a Menüben vagyunk, valószínûleg a régi "ragadt bent".
            // Két opció van:
            // A) Megöljük magunkat (de lehet a régi már "koszos").
            // B) Megöljük a régit, és mi leszünk az újak. <--- Ezt választjuk a tiszta startért.

            Debug.Log("[Lifecycle] Régi NetworkManager törlése, új inicializálása...");
            Destroy(NetworkManager.Singleton.gameObject);
        }
    }
}