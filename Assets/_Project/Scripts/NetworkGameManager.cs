using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    public enum GameState { Lobby, HunterRelease, InGame, HunterPanic, GameOver, MatchOver, EndGame }
    public NetworkVariable<GameState> currentGameState = new NetworkVariable<GameState>(GameState.Lobby);
    
    public NetworkVariable<bool> areTrapsActive = new NetworkVariable<bool>(false);

    [Header("Kör Esélyek (0-100%)")]
    [Tooltip("Sima kör esélye (csak kaja)")]
    [SerializeField] private int chanceNormal = 70;
    [Tooltip("Aknás kör esélye")]
    [SerializeField] private int chanceMines = 15;
    [Tooltip("Csapdás kör esélye")]
    [SerializeField] private int chanceTraps = 15;

    public NetworkVariable<int> hunterWins = new NetworkVariable<int>(0);
    public NetworkVariable<int> deerWins = new NetworkVariable<int>(0);
    public NetworkVariable<int> targetWins = new NetworkVariable<int>(3);

    private ulong currentHunterId;

    public enum RoundType { Normal, Mines, Traps }
    public NetworkVariable<RoundType> currentRoundType = new NetworkVariable<RoundType>(RoundType.Normal);

    private Dictionary<ulong, PlayerNetworkController> connectedPlayers = new Dictionary<ulong, PlayerNetworkController>();
    private List<SafeZone> allSafeZones = new List<SafeZone>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }
    public void RegisterPlayer(ulong clientId, PlayerNetworkController playerScript)
    {
        if (!connectedPlayers.ContainsKey(clientId))
        {
            connectedPlayers.Add(clientId, playerScript);
        }
    }
    private void OnClientDisconnect(ulong clientId)
    {
        if (IsServer)
        {
            if (connectedPlayers.ContainsKey(clientId))
            {
                connectedPlayers.Remove(clientId);
            }

            // [JAVÍTÁS] Ha játék közben lép ki valaki, vége a meccsnek!
            if (currentGameState.Value == GameState.InGame || currentGameState.Value == GameState.HunterRelease)
            {
                Debug.LogWarning($"Player {clientId} disconnected mid-game. Ending match.");

                // Értesítjük a többieket
                EndGameClientRpc("DRAW - PLAYER DISCONNECTED");

                // Vissza a Lobbyba kis késleltetéssel
                StartCoroutine(ReturnToLobbyRoutine());
            }
        }
    }
    [ServerRpc]
    public void StartGameServerRpc()
    {
        if (currentGameState.Value != GameState.Lobby) return;

        areTrapsActive.Value = false;
        AssignRoles();

        currentGameState.Value = GameState.HunterRelease;
    }
    private void AssignRoles()
    {
        var clientIds = new List<ulong>(connectedPlayers.Keys);
        if (clientIds.Count == 0) return;

        // Véletlenszerû Hunter
        int hunterIndex = Random.Range(0, clientIds.Count);
        currentHunterId = clientIds[hunterIndex];

        foreach (var kvp in connectedPlayers)
        {
            bool isHunter = (kvp.Key == currentHunterId);
            kvp.Value.isHunter.Value = isHunter;

            // Mindenkinek reseteljük a HP-ját és állapotát kezdéskor
            kvp.Value.ResetPlayerStateClientRpc();
        }
    }
    private IEnumerator HunterReleaseRoutine()
    {
        yield return new WaitForSeconds(15f);
        currentGameState.Value = GameState.InGame;
    }
    public void OnPlayerDied(ulong victimId, bool wasHunter, bool isInstaKill)
    {
        if (!IsServer) return;
        if (wasHunter)
        {
            if (isInstaKill) EndGameServerRpc(false);
            else TriggerHunterPanicMode(victimId);
        }
        else
        {
            if (connectedPlayers.TryGetValue(victimId, out var playerScript)) playerScript.SetGhostModeClientRpc();
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
                sortedZones[0].SetActive(true);
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
        // 1. Pontszám növelés
        if (hunterWon) hunterWins.Value++;
        else deerWins.Value++;

        if (GameSessionSettings.Instance != null)
        {
            GameSessionSettings.Instance.CurrentHunterScore = hunterWins.Value;
            GameSessionSettings.Instance.CurrentDeerScore = deerWins.Value;
        }

        // 2. Ellenõrizzük, vége-e a TELJES MECCSNEK?
        if (hunterWins.Value >= targetWins.Value || deerWins.Value >= targetWins.Value)
        {
            currentGameState.Value = GameState.MatchOver;

            // Meccs vége -> Reseteljük a mentett pontokat 0-ra, 
            // hogy ha visszalépnek a menübe vagy új meccset indítanak, tiszta lap legyen.
            if (GameSessionSettings.Instance != null)
            {
                GameSessionSettings.Instance.ResetScores();
            }

            string winner = hunterWon ? "VADÁSZ" : "SZARVASOK";
            ShowMatchOverUIClientRpc(winner);
        }
        else
        {
            // Sima kör vége
            currentGameState.Value = GameState.GameOver;
            ShowWinUIClientRpc(hunterWon ? "KÖR NYERTESE: VADÁSZ" : "KÖR NYERTESE: SZARVASOK");
            StartCoroutine(RestartGameRoutine());
        }
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
        yield return new WaitForSeconds(5f);
        NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            currentGameState.Value = GameState.Lobby;
        }
    }
    private void OnScoreChanged(int oldVal, int newVal)
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.UpdateScores(hunterWins.Value, deerWins.Value, targetWins.Value);
        }
    }
    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
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
        currentGameState.Value = GameState.InGame;
        areTrapsActive.Value = true;
    }
    public void CheckWinCondition()
    {
        if (!IsServer) return;
        if (currentGameState.Value != GameState.InGame) return;

        // Számoljuk meg az élõ szarvasokat
        int aliveDeers = 0;
        foreach (var kvp in connectedPlayers)
        {
            // Ha NEM Hunter és él
            if (!kvp.Value.isHunter.Value)
            {
                var health = kvp.Value.GetComponent<HealthComponent>();
                if (health != null && health.currentHealth.Value > 0)
                {
                    aliveDeers++;
                }
            }
        }

        if (aliveDeers == 0)
        {
            EndMatch("HUNTER WINS!");
        }
    }
    public void EndMatch(string winnerText)
    {
        if (!IsServer) return;

        currentGameState.Value = GameState.EndGame;
        EndGameClientRpc(winnerText);

        StartCoroutine(ReturnToLobbyRoutine());
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
        SetGlobalDrain(false);
        areTrapsActive.Value = false;
        if (LevelGenerator.Instance != null) LevelGenerator.Instance.ClearLevel();
        foreach (var player in connectedPlayers.Values)
        {
            player.isHunter.Value = false;
            var health = player.GetComponent<HealthComponent>();
            if (health != null)
            {
                health.isHunter = false;
                health.currentHealth.Value = 100f;
                health.isDraining.Value = false;
            }
            player.ResetPlayerStateClientRpc();
        }
    }
    private IEnumerator ArmTrapsRoutine()
    {
        yield return new WaitForSeconds(3f); // 3 mp késleltetés

        areTrapsActive.Value = true;

        // [JAVÍTÁS] Pontos üzenet meghatározása
        string msg = "";

        switch (currentRoundType.Value)
        {
            case RoundType.Mines:
                msg = "MINES ARMED! / AKNÁK ÉLESÍTVE!";
                break;
            case RoundType.Traps:
                msg = "TRAPS ARMED! / CSAPDÁK ÉLESÍTVE!";
                break;
                // Normal eset ide elvileg be se jut, de ha mégis, üres marad a msg
        }

        if (!string.IsNullOrEmpty(msg))
        {
            ShowTrapNotificationClientRpc(msg);
        }
    }
    [ClientRpc]
    private void EnableHuntersShootingClientRpc(bool enabled)
    {
        if (NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().TryGetComponent(out HunterShootingSystem shooting))
        {
            shooting.EnableShooting(enabled);
        }
    }
    [ClientRpc]
    private void ShowTrapNotificationClientRpc(string msg)
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.ShowNotification(msg);
        }
    }
    [ClientRpc]
    private void ShowMatchOverUIClientRpc(string winnerTeam)
    {
        if (GameHUD.Instance != null)
        {
            // Csak a HOST lássa a gombokat, a többiek csak a feliratot
            bool isHost = IsServer;
            GameHUD.Instance.ShowMatchOverScreen(winnerTeam, isHost);
        }
    }
    [ClientRpc]
    private void EndGameClientRpc(string winnerText)
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.ShowWinScreen(winnerText);
        }
    }
    public void LoadNextMap()
    {
        if (!IsServer) return;

        string nextSceneName = "";

        // Ha Random be volt pipálva, vagy "Váltás" a cél -> Sorsolunk újat
        if (GameSessionSettings.Instance != null)
        {
            // Sorsolunk egy pályát (lehetõleg ne a mostanit)
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            int attempts = 0;
            do
            {
                nextSceneName = GameSessionSettings.Instance.GetRandomMap();
                attempts++;
            } while (nextSceneName == currentScene && attempts < 5 && GameSessionSettings.Instance.availableMaps.Count > 1);
        }
        else
        {
            // Ha nem random, akkor is váltsunk? A kérés: "ha folytatás akkor váltson map-t".
            // Akkor is sorsoljunk egyet a listából.
            nextSceneName = GameSessionSettings.Instance.GetRandomMap();
        }

        Debug.Log($"[MatchOver] Loading next map: {nextSceneName}");
        NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
    private IEnumerator ReturnToLobbyRoutine()
    {
        yield return new WaitForSeconds(5f); // 5 másodpercig mutatjuk az eredményt

        // Scene újratöltése a szerveren (ez mindenkinél újratölti)
        // Fontos: A GameLoopManager OnSceneLoaded-je fogja kezelni az újracsatlakozást
        NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

}