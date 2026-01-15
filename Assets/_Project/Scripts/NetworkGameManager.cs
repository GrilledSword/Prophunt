using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

// Profi tipp: Singleton minta a könnyû eléréshez, de NetworkBehaviour-be csomagolva.
public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    // State management: Lobby, InGame, Ending
    public enum GameState { Lobby, InGame, GameOver }
    private NetworkVariable<GameState> currentGameState = new NetworkVariable<GameState>(GameState.Lobby);

    // Lista a csatlakozott játékosokról
    private Dictionary<ulong, PlayerNetworkController> connectedPlayers = new Dictionary<ulong, PlayerNetworkController>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        Instance = this;
    }

    // Amikor egy játékos csatlakozik, regisztráljuk
    public void RegisterPlayer(ulong clientId, PlayerNetworkController playerScript)
    {
        if (!connectedPlayers.ContainsKey(clientId))
        {
            connectedPlayers.Add(clientId, playerScript);
            Debug.Log($"[Server] Játékos regisztrálva: {clientId}");
        }
    }

    // Keresd meg a StartGameServerRpc metódust, és írd át a foreach ciklust:
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        if (!IsServer) return;

        Debug.Log("[Server] Játék indítása... Hunter sorsolása.");

        // currentGameState.Value = GameState.InGame; // Ha használod a state-et

        List<ulong> clientIds = connectedPlayers.Keys.ToList();
        if (clientIds.Count == 0) return;

        ulong hunterId = clientIds[Random.Range(0, clientIds.Count)];

        foreach (var player in connectedPlayers)
        {
            bool isHunter = (player.Key == hunterId);

            // [JAVÍTÁS] Nem hívunk RPC-t! Közvetlenül beállítjuk az értéket.
            // Mivel Serveren vagyunk, és a NetworkVariable WritePermission-je Server, ezt megtehetjük.
            player.Value.isHunter.Value = isHunter;

            Debug.Log($"[Server] {player.Key} szerepe beállítva: {(isHunter ? "HUNTER" : "DEER")}");
        }
    }
    public override void OnNetworkSpawn()
    {
        // [ÚJ] Amikor a GameManager létrejön (Pálya betöltéskor),
        // körbenézünk, hogy vannak-e már játékosok, akik "kimaradtak" a regisztrációból.
        if (IsServer)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null)
                {
                    var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
                    if (playerScript != null)
                    {
                        RegisterPlayer(client.ClientId, playerScript);
                    }
                }
            }
        }

        // Iratkozzunk fel a késõbbi csatlakozókra is, biztos ami biztos
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    // Takarítás, ha megszûnik a GameManager
    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // Ez a normál csatlakozásokat kezeli majd játék közben
        if (IsServer && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
                RegisterPlayer(clientId, playerScript);
            }
        }
    }
}