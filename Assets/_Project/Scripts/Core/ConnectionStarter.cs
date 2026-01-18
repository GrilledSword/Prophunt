using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP; // IP cím beállításához kell

public class ConnectionStarter : MonoBehaviour
{
    private void Start()
    {
        // Ha nincs NetworkManager, baj van
        if (NetworkManager.Singleton == null) return;

        // Ha nincs Settings (pl. közvetlenül az Editorból indítottad a GameScene-t),
        // akkor ne csináljon semmit, vagy induljon el alapértelmezetten.
        if (GameSessionSettings.Instance == null)
        {
            Debug.LogWarning("Nincs GameSessionSettings! (Direct start?)");
            return;
        }

        // Döntés: Host vagy Kliens?
        if (GameSessionSettings.Instance.ShouldStartAsHost)
        {
            NetworkManager.Singleton.StartHost();
        }
        else
        {
            Debug.Log($"Starting Client connecting to {GameSessionSettings.Instance.TargetIPAddress}...");

            // IP cím beállítása a Transportban
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = GameSessionSettings.Instance.TargetIPAddress;
            }

            NetworkManager.Singleton.StartClient();
        }
    }
}