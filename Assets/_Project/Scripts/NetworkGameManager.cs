using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    public enum GameState { Lobby, HunterRelease, InGame, HunterPanic, GameOver }
    public NetworkVariable<GameState> currentGameState = new NetworkVariable<GameState>(GameState.Lobby);

    private Dictionary<ulong, PlayerNetworkController> connectedPlayers = new Dictionary<ulong, PlayerNetworkController>();
    private List<SafeZone> allSafeZones = new List<SafeZone>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }
    public void RegisterPlayer(ulong clientId, PlayerNetworkController playerScript)
    {
        if (!connectedPlayers.ContainsKey(clientId)) connectedPlayers.Add(clientId, playerScript);
    }
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        if (!IsServer) return;
        currentGameState.Value = GameState.HunterRelease;
        SetGlobalDrain(false); // Release alatt MÉG NEM fogy!
        List<ulong> clientIds = connectedPlayers.Keys.ToList();
        if (clientIds.Count == 0) return;
        ulong hunterId = clientIds[Random.Range(0, clientIds.Count)];

        foreach (var player in connectedPlayers)
        {
            bool isHunter = (player.Key == hunterId);
            player.Value.isHunter.Value = isHunter;
            var health = player.Value.GetComponent<HealthComponent>();
            if (health != null) health.isHunter = isHunter; // Biztos ami biztos
        }
        StartCoroutine(HunterReleaseRoutine());
    }
    private IEnumerator HunterReleaseRoutine()
    {
        yield return new WaitForSeconds(15f);
        currentGameState.Value = GameState.InGame;
        Debug.Log("VADÁSZ SZABADON! (IN GAME)");
        // Itt jöhetne egy hang: "Ready or not, here I come!"
    }
    public void OnPlayerDied(ulong victimId, bool wasHunter, bool isInstaKill)
    {
        if (!IsServer) return;

        if (wasHunter)
        {
            if (isInstaKill)
            {
                EndGameServerRpc(false); // Vadász meghalt -> Deer Win
            }
            else
            {
                TriggerHunterPanicMode(victimId);
            }
        }
        else
        {
            if (connectedPlayers.TryGetValue(victimId, out var playerScript))
            {
                playerScript.SetGhostModeClientRpc();
            }
            CheckDeerWinCondition();
        }
    }
    private void TriggerHunterPanicMode(ulong hunterId)
    {
        currentGameState.Value = GameState.HunterPanic;

        if (connectedPlayers.TryGetValue(hunterId, out var hunterScript))
        {
            hunterScript.TransformToPanicModeClientRpc();

            if (allSafeZones.Count > 0)
            {
                var sortedZones = allSafeZones.OrderByDescending(z => Vector3.Distance(z.transform.position, hunterScript.transform.position)).ToList();
                SafeZone selectedZone = sortedZones[0];

                selectedZone.SetActive(true);
                Debug.Log($"[PANIC] SafeZone aktiválva: {selectedZone.name}");
            }
        }
    }
    private void CheckDeerWinCondition()
    {
        int livingDeer = 0;
        foreach (var p in connectedPlayers.Values)
        {
            if (!p.isHunter.Value && p.GetComponent<HealthComponent>().currentHealth.Value > 0) livingDeer++;
        }
        if (livingDeer == 0) EndGameServerRpc(true);
    }
    [ServerRpc]
    public void EndGameServerRpc(bool hunterWon)
    {
        currentGameState.Value = GameState.GameOver;

        // UI Megjelenítése mindenkinél
        ShowWinUIClientRpc(hunterWon ? "VADÁSZ NYERT!" : "SZARVASOK NYERTEK!");

        // Újraindítás késleltetve
        StartCoroutine(RestartGameRoutine());
    }
    [ClientRpc]
    private void ShowWinUIClientRpc(string winnerText)
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.ShowWinScreen(winnerText);
        }
    }
    private IEnumerator RestartGameRoutine()
    {
        yield return new WaitForSeconds(5f); // 5 mp ünneplés

        // Pálya újratöltése (Ez visszaviszi a játékosokat a Lobby állapotba a scriptjeik szerint)
        NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
    public override void OnNetworkSpawn()
    {
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
        if (IsServer)
        {
            currentGameState.Value = GameState.Lobby;

            // Safe Zone-ok kikapcsolása
            allSafeZones = FindObjectsOfType<SafeZone>().ToList();
            foreach (var zone in allSafeZones) zone.SetActive(false);

            // Mindenki szerepének törlése (Legyen mindenki Szarvas a Lobbyban)
            ResetAllPlayersToLobbyState();
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }
    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
    private void OnClientConnected(ulong clientId)
    {
        if (IsServer && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
                RegisterPlayer(clientId, playerScript);
            }
        }
    }
    public bool IsInGame()
    {
        return currentGameState.Value == GameState.InGame || currentGameState.Value == GameState.HunterPanic;
    }
    public bool IsHunterRelease() => currentGameState.Value == GameState.HunterRelease;
    public void SetHunterFree()
    {
        if (!IsServer) return;
        currentGameState.Value = GameState.InGame;
        SetGlobalDrain(true); // MOST INDUL A FOGYÁS!

        // [ÚJ] Engedélyezzük a lövést minden vadásznak
        EnableHuntersShootingClientRpc(true);

        Debug.Log("VADÁSZ SZABADON! MEHET A MENET!");
    }
    private void SetGlobalDrain(bool enabled)
    {
        foreach (var player in connectedPlayers.Values)
        {
            var health = player.GetComponent<HealthComponent>();
            if (health != null) health.isDraining.Value = enabled;
        }
    }
    public bool IsHunterPanic() => currentGameState.Value == GameState.HunterPanic;
    private void ResetAllPlayersToLobbyState()
    {
        // [FONTOS] Globális fogyás kikapcsolása!
        SetGlobalDrain(false);

        foreach (var player in connectedPlayers.Values)
        {
            player.isHunter.Value = false;

            var health = player.GetComponent<HealthComponent>();
            if (health != null)
            {
                health.isHunter = false;
                health.currentHealth.Value = 100f;
                health.isDraining.Value = false; // [FONTOS] Egyéni szinten is stop
            }

            player.ResetPlayerStateClientRpc();
        }
    }
    [ClientRpc]
    private void EnableHuntersShootingClientRpc(bool enabled)
    {
        // Megkeressük a helyi játékost
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent(out HunterShootingSystem shooting))
        {
            shooting.EnableShooting(enabled);
        }
    }
}