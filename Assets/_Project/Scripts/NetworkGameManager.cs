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

    // Csak a Host hívhatja meg ezt a játék indításához
    [ServerRpc(RequireOwnership = false)] // Bárki kérheti, de a szerver dönti el
    public void StartGameServerRpc()
    {
        if (!IsServer) return;

        Debug.Log("[Server] Játék indítása... Hunter sorsolása.");

        // 1. Állapot váltás
        currentGameState.Value = GameState.InGame;

        // 2. Véletlenszerû Hunter kiválasztása
        List<ulong> clientIds = connectedPlayers.Keys.ToList();
        if (clientIds.Count == 0) return;

        ulong hunterId = clientIds[Random.Range(0, clientIds.Count)];

        // 3. Mindenkinek elküldjük a szerepét
        foreach (var player in connectedPlayers)
        {
            // Ha õ a kiválasztott, akkor Hunter, amúgy Prey (Szarvas)
            bool isHunter = (player.Key == hunterId);
            player.Value.SetRoleServerRpc(isHunter);
        }
    }
}