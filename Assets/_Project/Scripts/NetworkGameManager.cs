using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    public enum GameState { Lobby, HunterRelease, InGame, HunterPanic, GameOver, MatchOver }
    public NetworkVariable<GameState> currentGameState = new NetworkVariable<GameState>(GameState.Lobby);
    
    public NetworkVariable<bool> areTrapsActive = new NetworkVariable<bool>(false);
    [SerializeField] private int chanceNormal = 70;
    [SerializeField] private int chanceMines = 15;
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
        if (!connectedPlayers.ContainsKey(clientId)) connectedPlayers.Add(clientId, playerScript);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartGameServerRpc()
    {
        if (!IsServer) return;
        currentGameState.Value = GameState.HunterRelease;
        SetGlobalDrain(false);
        areTrapsActive.Value = false;

        int totalWeight = chanceNormal + chanceMines + chanceTraps;
        int randomVal = Random.Range(0, totalWeight);

        if (randomVal < chanceNormal)
        {
            currentRoundType.Value = RoundType.Normal;
            Debug.Log("[NetworkGameManager] Selected RoundType: Normal");
        }
        else if (randomVal < chanceNormal + chanceMines)
        {
            currentRoundType.Value = RoundType.Mines;
            Debug.Log("[NetworkGameManager] Selected RoundType: Mines");
        }
        else
        {
            currentRoundType.Value = RoundType.Traps;
            Debug.Log("[NetworkGameManager] Selected RoundType: Traps");
        }

        // [REMOVED] GenerateLevel hívás - ezt a GameLoopManager kezeli!

        List<ulong> clientIds = connectedPlayers.Keys.ToList();
        if (clientIds.Count == 0) return;
        ulong hunterId = clientIds[Random.Range(0, clientIds.Count)];

        foreach (var player in connectedPlayers)
        {
            bool isHunter = (player.Key == hunterId);
            player.Value.isHunter.Value = isHunter;
            var health = player.Value.GetComponent<HealthComponent>();
            if (health != null) health.isHunter = isHunter;
        }
        StartCoroutine(HunterReleaseRoutine());
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
                // Optimize: Find the closest safe zone without OrderByDescending
                SafeZone closestZone = allSafeZones[0];
                float maxDistance = Vector3.Distance(closestZone.transform.position, hunterScript.transform.position);

                for (int i = 1; i < allSafeZones.Count; i++)
                {
                    float distance = Vector3.Distance(allSafeZones[i].transform.position, hunterScript.transform.position);
                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                        closestZone = allSafeZones[i];
                    }
                }
                closestZone.SetActive(true);
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
        if (hunterWon) hunterWins.Value++;
        else deerWins.Value++;

        if (GameSessionSettings.Instance != null)
        {
            GameSessionSettings.Instance.CurrentHunterScore = hunterWins.Value;
            GameSessionSettings.Instance.CurrentDeerScore = deerWins.Value;
        }

        if (hunterWins.Value >= targetWins.Value || deerWins.Value >= targetWins.Value)
        {
            currentGameState.Value = GameState.MatchOver;
            if (GameSessionSettings.Instance != null)
            {
                GameSessionSettings.Instance.ResetScores();
            }

            string winner = hunterWon ? "VADÁSZ" : "SZARVASOK";
            ShowMatchOverUIClientRpc(winner);
        }
        else
        {
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
        hunterWins.OnValueChanged += OnScoreChanged;
        deerWins.OnValueChanged += OnScoreChanged;

        if (GameHUD.Instance != null)
            GameHUD.Instance.UpdateScores(hunterWins.Value, deerWins.Value, targetWins.Value);

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        if (!IsServer) return;

        // Register all connected clients
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

        // Initialize server-side game state
        if (GameSessionSettings.Instance != null)
        {
            targetWins.Value = GameSessionSettings.Instance.TargetWins;
            hunterWins.Value = GameSessionSettings.Instance.CurrentHunterScore;
            deerWins.Value = GameSessionSettings.Instance.CurrentDeerScore;
        }

        currentGameState.Value = GameState.Lobby;
        areTrapsActive.Value = false;
        currentRoundType.Value = RoundType.Normal;

        // Setup safe zones - Use FindObjectsByType for better performance
        allSafeZones = FindObjectsByType<SafeZone>(FindObjectsSortMode.None).ToList();
        foreach (var zone in allSafeZones) zone.SetActive(false);

        ResetAllPlayersToLobbyState();
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
        SetGlobalDrain(true);
        EnableHuntersShootingClientRpc(true);
        if (currentRoundType.Value != RoundType.Normal)
        {
            StartCoroutine(ArmTrapsRoutine());
        }
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
        // [REMOVED] ClearLevel() az GameLoopManager-ben hívódik meg, ne duplázódjon!
        
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
        yield return new WaitForSeconds(3f);

        areTrapsActive.Value = true;
        string msg = "";

        switch (currentRoundType.Value)
        {
            case RoundType.Mines:
                msg = "MINES ARMED! / AKNÁK ÉLESÍTVE!";
                break;
            case RoundType.Traps:
                msg = "TRAPS ARMED! / CSAPDÁK ÉLESÍTVE!";
                break;
        }

        if (!string.IsNullOrEmpty(msg))
        {
            ShowTrapNotificationClientRpc(msg);
        }
    }
    [ClientRpc]
    private void EnableHuntersShootingClientRpc(bool enabled)
    {
        var localPlayer = NetworkManager.Singleton?.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null && localPlayer.TryGetComponent(out HunterShootingSystem shooting))
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
            bool isHost = IsServer;
            GameHUD.Instance.ShowMatchOverScreen(winnerTeam, isHost);
        }
    }
    public void LoadNextMap()
    {
        if (!IsServer) return;

        string nextSceneName = "";
        if (GameSessionSettings.Instance != null)
        {
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
            Debug.LogError("[NetworkGameManager] GameSessionSettings.Instance is null!");
            return;
        }

        Debug.Log($"[MatchOver] Loading next map: {nextSceneName}");
        NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

}