using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public class GameLoopManager : NetworkBehaviour
{
    public static GameLoopManager Instance { get; private set; }

    public enum LoadingState { WaitingForConnection, LoadingScene, InitializingMap, Ready }
    private LoadingState currentLoadingState = LoadingState.WaitingForConnection;

    [Header("Idõzítés")]
    [SerializeField] private float lobbyTime = 10f;
    [SerializeField] private float hunterReleaseTime = 15f;
    [SerializeField] private int minPlayersToStart = 1;

    [Header("UI")]
    [SerializeField] private GameObject lobbyUI;
    [SerializeField] private GameObject loadingScreenPanel;

    private NetworkVariable<float> currentTimer = new NetworkVariable<float>(0f);
    private bool isLobbyTimerRunning = false;
    private bool isReleaseTimerRunning = false;
    private bool isMatchStarted = false;

    private bool isSceneReloaded = false;
    private bool areClientsLoaded = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }
    }
    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
        }
        base.OnDestroy();
    }
    public override void OnNetworkSpawn()
    {
        // --- KLIENT OLDALI JAVÍTÁS ---
        // Ha nem vagyunk szerver (tehát kliensek vagyunk), és épp most spawnoltunk le:
        if (!IsServer)
        {
            // Azonnal ellenõrizzük, hogy el kell-e tüntetni a Loading Screent.
            // Ha a NetworkGameManager már létezik (márpedig szinkronizálva van), és nem "Lobby"-ban vagyunk, vagy már készen van a pálya...
            // Egyszerûbb logika: Ha ez a szkript fut a kliensen, az azt jelenti, hogy a Scene betöltött.
            // Várunk egy kicsit a biztonság kedvéért, vagy elrejtjük, ha a játékállapot engedi.
            CheckClientLoadingScreen();
        }

        if (IsServer)
        {
            areClientsLoaded = false;
            if (NetworkManager.Singleton.ConnectedClientsList.Count == 1)
            {
                Debug.Log("[GameLoop] Solo Host detected, skipping wait.");
                areClientsLoaded = true;
            }

            if (GameSessionSettings.Instance != null)
            {
                minPlayersToStart = GameSessionSettings.Instance.MinPlayersToStart;
            }

            currentLoadingState = LoadingState.LoadingScene;
        }

        currentTimer.OnValueChanged += OnTimerChanged;

        // Klienseknél figyeljük a Timer változást is, az jó jelzõ arra, hogy él a kapcsolat
        if (loadingScreenPanel != null) loadingScreenPanel.SetActive(true);
    }
    private void CheckClientLoadingScreen()
    {
        // Ha már csatlakoztunk és megkaptuk a játékállapotot, és az nem 'Inaktív', akkor levehetjük a töltõképernyõt.
        // Még jobb: Ha a NetworkGameManager szerint a játékállapot Lobby, InGame, vagy bármi 'élõ', akkor mehet.
        if (NetworkGameManager.Instance != null)
        {
            // Ha már bejutottunk a Lobbyba vagy a Játékba, tüntesd el!
            HideLoadingScreenClientRpc();
        }
        else
        {
            // Ha még nincs NetworkGameManager (ritka), próbálkozzunk késõbb
            StartCoroutine(WaitForGameState());
        }
    }
    private IEnumerator WaitForGameState()
    {
        yield return new WaitForSeconds(0.5f);
        if (loadingScreenPanel != null) loadingScreenPanel.SetActive(false);
    }
    private void OnSceneLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        areClientsLoaded = true;
        isSceneReloaded = true;
    }
    private void Update()
    {
        // [ÚJ] Kliens oldali "Fail-safe": Ha véletlenül fent maradt a loading screen, de már megy a játék.
        if (!IsServer && loadingScreenPanel != null && loadingScreenPanel.activeSelf)
        {
            // Ha már látjuk a Lobby UI-t, akkor a Loading Screen biztos nem kell.
            if (lobbyUI != null && lobbyUI.activeSelf)
            {
                loadingScreenPanel.SetActive(false);
            }
        }

        if (!IsServer) return;

        // --- SERVER LOADING STATE MACHINE ---
        switch (currentLoadingState)
        {
            case LoadingState.WaitingForConnection:
                break;

            case LoadingState.LoadingScene:
                if (MapSettings.Instance != null && areClientsLoaded)
                {
                    currentLoadingState = LoadingState.InitializingMap;
                }
                break;

            case LoadingState.InitializingMap:
                TeleportAllToLobby();

                if (NetworkGameManager.Instance != null)
                    NetworkGameManager.Instance.currentGameState.Value = NetworkGameManager.GameState.Lobby;

                ResetUIClientRpc();
                HideLoadingScreenClientRpc();

                currentLoadingState = LoadingState.Ready;
                break;

            case LoadingState.Ready:
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

        // --- LOBBY LOGIKA ---
        if (!isMatchStarted)
        {
            int playerCount = NetworkManager.Singleton.ConnectedClientsList.Count;

            if (playerCount < minPlayersToStart)
            {
                if (isLobbyTimerRunning)
                {
                    isLobbyTimerRunning = false;
                    currentTimer.Value = 0;
                }
                UpdateWaitingUIClientRpc(playerCount, minPlayersToStart);
            }
            else
            {
                if (!isLobbyTimerRunning)
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
        }
        else if (isReleaseTimerRunning)
        {
            currentTimer.Value -= Time.deltaTime;

            // [JAVÍTÁS] Ha lejárt a Release Timer
            if (currentTimer.Value <= 0f)
            {
                currentTimer.Value = 0f; // Biztos ami biztos nullázzuk
                isReleaseTimerRunning = false;

                MoveHunterToOutside();
                NetworkGameManager.Instance.SetHunterFree();

                // [JAVÍTÁS] Kényszerítjük a UI eltüntetését mindenkinél, mert a Timer 0 értéke önmagában már nem teszi meg
                ClearTimerUIClientRpc();
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
        if (LevelGenerator.Instance != null)
        {
            var roundTypes = System.Enum.GetValues(typeof(NetworkGameManager.RoundType));
            NetworkGameManager.RoundType randomType = (NetworkGameManager.RoundType)roundTypes.GetValue(Random.Range(0, roundTypes.Length));
            LevelGenerator.Instance.GenerateLevel(randomType);
        }

        DistributePlayersToSpawnPoints();
        ToggleLobbyUIClientRpc(false);

        isReleaseTimerRunning = true;
        currentTimer.Value = hunterReleaseTime;
    }
    private void DistributePlayersToSpawnPoints()
    {
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
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { clientId } }
        };
        ForceTeleportClientRpc(position, clientRpcParams);
    }
    private void OnTimerChanged(float oldVal, float newVal)
    {
        if (GameHUD.Instance == null) return;

        // [JAVÍTÁS] Finomított logika
        if (newVal > 0)
        {
            bool isReleasePhase = false;
            if (NetworkGameManager.Instance != null)
                isReleasePhase = NetworkGameManager.Instance.currentGameState.Value == NetworkGameManager.GameState.HunterRelease;

            string prefix = isReleasePhase ? "RELEASE: " : "START: ";
            string colorHex = isReleasePhase ? "<color=red>" : "<color=white>";

            // Csak akkor írjuk ki, ha nem Waiting szövegnek kellene lennie
            // De mivel a Waiting szöveg RPC-vel jön, itt egyszerûen felülírjuk, ha van Timer
            GameHUD.Instance.UpdateTimer($"{colorHex}{prefix}{Mathf.CeilToInt(newVal)}</color>");
        }
        else
        {
            // Ha a Timer 0, két eset van:
            // 1. Lobbyban vagyunk és várunk -> Ezt az UpdateWaitingUIClientRpc kezeli, NE töröljük
            // 2. HunterRelease vége (InGame eleje) -> Ezt törölni KELL.

            if (NetworkGameManager.Instance != null &&
                NetworkGameManager.Instance.currentGameState.Value == NetworkGameManager.GameState.HunterRelease)
            {
                GameHUD.Instance.UpdateTimer("");
            }
        }
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
    [ClientRpc]
    private void ForceTeleportClientRpc(Vector3 position, ClientRpcParams clientRpcParams = default)
    {
        var localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer != null)
        {
            var characterController = localPlayer.GetComponent<CharacterController>();
            if (characterController != null) characterController.enabled = false;
            localPlayer.transform.position = position;
            if (characterController != null) characterController.enabled = true;
        }
    }
    [ClientRpc]
    private void UpdateWaitingUIClientRpc(int current, int required)
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.UpdateTimer($"<color=yellow>WAITING FOR PLAYERS: {current}/{required}</color>");
        }
    }
    [ClientRpc]
    private void ClearTimerUIClientRpc()
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.UpdateTimer("");
        }
    }
}