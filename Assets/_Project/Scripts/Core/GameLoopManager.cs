using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public class GameLoopManager : NetworkBehaviour
{
    public static GameLoopManager Instance { get; private set; }

    // [ÚJ] Loading Állapotok
    public enum LoadingState { WaitingForConnection, LoadingScene, InitializingMap, Ready }
    private LoadingState currentLoadingState = LoadingState.WaitingForConnection;

    [Header("Idõzítés")]
    [SerializeField] private float lobbyTime = 10f;
    [SerializeField] private float hunterReleaseTime = 15f;
    [SerializeField] private int minPlayersToStart = 1;

    [Header("UI")]
    [SerializeField] private GameObject lobbyUI;
    [SerializeField] private GameObject loadingScreenPanel; // [ÚJ] Loading Screen

    private NetworkVariable<float> currentTimer = new NetworkVariable<float>(0f);
    private bool isLobbyTimerRunning = false;
    private bool isReleaseTimerRunning = false;
    private bool isMatchStarted = false;

    // Restart flag
    private bool isSceneReloaded = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Beállítások átvétele
            if (GameSessionSettings.Instance != null)
            {
                minPlayersToStart = GameSessionSettings.Instance.MinPlayersToStart;
            }

            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;

            // Start Loading Sequence
            currentLoadingState = LoadingState.LoadingScene;
        }

        currentTimer.OnValueChanged += OnTimerChanged;

        // Klienseknél is bekapcsoljuk a Loading Screen-t amíg nem kapnak más utasítást
        if (loadingScreenPanel != null) loadingScreenPanel.SetActive(true);
    }
    private void OnSceneLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;

        // Ez a callback minden kliens betöltésekor lefut, de nekünk elég ha tudjuk, hogy a folyamat zajlik.
        // A valódi inicializálást az Update-ben a State Machine végzi.
        isSceneReloaded = true;
    }
    private void Update()
    {
        if (!IsServer) return;

        // --- LOADING STATE MACHINE ---
        switch (currentLoadingState)
        {
            case LoadingState.WaitingForConnection:
                // Várunk, amíg a NetworkManager elindul (ez már OnNetworkSpawn-nál megtörtént)
                break;

            case LoadingState.LoadingScene:
                // Várunk, amíg a MapSettings elérhetõ lesz (betöltött a scene)
                if (MapSettings.Instance != null)
                {
                    currentLoadingState = LoadingState.InitializingMap;
                }
                break;

            case LoadingState.InitializingMap:
                // Most már biztosan megvan minden pont, teleportálhatunk
                TeleportAllToLobby();

                // Reseteljük a játékállapotot
                if (NetworkGameManager.Instance != null)
                    NetworkGameManager.Instance.currentGameState.Value = NetworkGameManager.GameState.Lobby;

                // UI Reset mindenkinél
                ResetUIClientRpc();
                HideLoadingScreenClientRpc(); // [FONTOS] Itt tûnik el a loading screen!

                currentLoadingState = LoadingState.Ready;
                break;

            case LoadingState.Ready:
                // Mehet a normál játékmenet (Timer, stb.)
                HandleGameLoop();
                break;
        }
    }
    private void HandleGameLoop()
    {
        if (!IsServer) return;

        if (isSceneReloaded && IsSpawned)
        {
            isSceneReloaded = false;
            ResetUIClientRpc();
            if (NetworkGameManager.Instance != null)
                NetworkGameManager.Instance.currentGameState.Value = NetworkGameManager.GameState.Lobby;
        }

        // Lobby Timer
        if (!isMatchStarted)
        {
            int playerCount = NetworkManager.Singleton.ConnectedClientsList.Count;
            if (playerCount >= minPlayersToStart && !isLobbyTimerRunning)
            {
                isLobbyTimerRunning = true;
                currentTimer.Value = lobbyTime;
            }

            if (isLobbyTimerRunning)
            {
                currentTimer.Value -= Time.deltaTime;
                if (currentTimer.Value <= 0f) StartMatchSequence();
            }
        }
        // Release Timer
        else if (isReleaseTimerRunning)
        {
            currentTimer.Value -= Time.deltaTime;
            if (currentTimer.Value <= 0f)
            {
                isReleaseTimerRunning = false;
                MoveHunterToOutside();
                NetworkGameManager.Instance.SetHunterFree();
            }
        }
    }
    private void TeleportAllToLobby()
    {
        if (MapSettings.Instance == null || MapSettings.Instance.LobbySpawnPoint == null) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            TeleportPlayer(client.ClientId, MapSettings.Instance.LobbySpawnPoint.position);
        }
    }
    private void StartMatchSequence()
    {
        isLobbyTimerRunning = false;
        isMatchStarted = true;

        NetworkGameManager.Instance.StartGameServerRpc();
        DistributePlayersToSpawnPoints();
        ToggleLobbyUIClientRpc(false);

        isReleaseTimerRunning = true;
        currentTimer.Value = hunterReleaseTime;
    }
    private void DistributePlayersToSpawnPoints()
    {
        // Ha valamiért nincs MapSettings (lehetetlen, de biztos ami biztos)
        if (MapSettings.Instance == null) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
            if (playerScript == null) continue;

            Vector3 targetPos = Vector3.zero;

            if (playerScript.isHunter.Value)
            {
                if (MapSettings.Instance.HunterCabinSpawnPoint != null)
                    targetPos = MapSettings.Instance.HunterCabinSpawnPoint.position;
            }
            else
            {
                if (MapSettings.Instance.DeerSpawnPoint != null)
                {
                    Vector3 randomOffset = new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
                    targetPos = MapSettings.Instance.DeerSpawnPoint.position + randomOffset;
                }
            }

            TeleportPlayer(client.ClientId, targetPos);
        }
    }
    private void TeleportPlayer(ulong clientId, Vector3 position)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            var characterController = client.PlayerObject.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;
            client.PlayerObject.transform.position = position;
            if (characterController != null) characterController.enabled = true;
        }
    }
    private void OnTimerChanged(float oldVal, float newVal)
    {
        if (GameHUD.Instance == null) return;
        if (newVal > 0)
        {
            bool isReleasePhase = false;
            if (NetworkGameManager.Instance != null)
                isReleasePhase = NetworkGameManager.Instance.currentGameState.Value == NetworkGameManager.GameState.HunterRelease;

            string prefix = isReleasePhase ? "RELEASE: " : "START: ";
            string colorHex = isReleasePhase ? "<color=red>" : "<color=white>";
            GameHUD.Instance.UpdateTimer($"{colorHex}{prefix}{Mathf.CeilToInt(newVal)}</color>");
        }
        else GameHUD.Instance.UpdateTimer("");
    }
    private void MoveHunterToOutside()
    {
        if (MapSettings.Instance == null || MapSettings.Instance.HunterReleaseSpawnPoint == null) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
            if (playerScript != null && playerScript.isHunter.Value)
            {
                TeleportPlayer(client.ClientId, MapSettings.Instance.HunterReleaseSpawnPoint.position);
            }
        }
    }
    [ClientRpc] private void ToggleLobbyUIClientRpc(bool isActive) { if (lobbyUI != null) lobbyUI.SetActive(isActive); }

    [ClientRpc]
    private void ResetUIClientRpc()
    {
        if (GameHUD.Instance != null) GameHUD.Instance.ResetWinScreen();
        if (lobbyUI != null) lobbyUI.SetActive(true);
    }
    [ClientRpc]
    private void HideLoadingScreenClientRpc()
    {
        if (loadingScreenPanel != null) loadingScreenPanel.SetActive(false);
    }
}