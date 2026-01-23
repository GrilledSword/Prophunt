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

    [Header("Id�z�t�s")]
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
        // --- KLIENT OLDALI JAV�T�S ---
        // Ha nem vagyunk szerver (teh�t kliensek vagyunk), �s �pp most spawnoltunk le:
        if (!IsServer)
        {
            // Azonnal ellen�rizz�k, hogy el kell-e t�ntetni a Loading Screent.
            // Ha a NetworkGameManager m�r l�tezik (m�rpedig szinkroniz�lva van), �s nem "Lobby"-ban vagyunk, vagy m�r k�szen van a p�lya...
            // Egyszer�bb logika: Ha ez a szkript fut a kliensen, az azt jelenti, hogy a Scene bet�lt�tt.
            // V�runk egy kicsit a biztons�g kedv��rt, vagy elrejtj�k, ha a j�t�k�llapot engedi.
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

        // Kliensekn�l figyelj�k a Timer v�ltoz�st is, az j� jelz� arra, hogy �l a kapcsolat
        if (loadingScreenPanel != null) loadingScreenPanel.SetActive(true);
    }
    private void CheckClientLoadingScreen()
    {
        // Ha m�r csatlakoztunk �s megkaptuk a j�t�k�llapotot, �s az nem 'Inakt�v', akkor levehetj�k a t�lt�k�perny�t.
        // M�g jobb: Ha a NetworkGameManager szerint a j�t�k�llapot Lobby, InGame, vagy b�rmi '�l�', akkor mehet.
        if (NetworkGameManager.Instance != null)
        {
            // Ha m�r bejutottunk a Lobbyba vagy a J�t�kba, t�ntesd el!
            HideLoadingScreenClientRpc();
        }
        else
        {
            // Ha m�g nincs NetworkGameManager (ritka), pr�b�lkozzunk k�s�bb
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
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        areClientsLoaded = true;
        isSceneReloaded = true;
    }
    private void Update()
    {
        // [�J] Kliens oldali "Fail-safe": Ha v�letlen�l fent maradt a loading screen, de m�r megy a j�t�k.
        if (!IsServer && loadingScreenPanel != null && loadingScreenPanel.activeSelf)
        {
            // Ha m�r l�tjuk a Lobby UI-t, akkor a Loading Screen biztos nem kell.
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
            
            // [NEW] Takarítás az új kör előtt
            if (LevelGenerator.Instance != null)
            {
                LevelGenerator.Instance.ClearLevel();
                Debug.Log("[GameLoop] Pálya megtisztítva az új kör előtt!");
            }
            
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

            // [JAV�T�S] Ha lej�rt a Release Timer
            if (currentTimer.Value <= 0f)
            {
                currentTimer.Value = 0f; // Biztos ami biztos null�zzuk
                isReleaseTimerRunning = false;

                MoveHunterToOutside();
                NetworkGameManager.Instance.SetHunterFree();

                // [JAV�T�S] K�nyszer�tj�k a UI elt�ntet�s�t mindenkin�l, mert a Timer 0 �rt�ke �nmag�ban m�r nem teszi meg
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

        // [JAV�T�S] Finom�tott logika
        if (newVal > 0)
        {
            bool isReleasePhase = false;
            if (NetworkGameManager.Instance != null)
                isReleasePhase = NetworkGameManager.Instance.currentGameState.Value == NetworkGameManager.GameState.HunterRelease;

            string prefix = isReleasePhase ? "RELEASE: " : "START: ";
            string colorHex = isReleasePhase ? "<color=red>" : "<color=white>";

            // Csak akkor �rjuk ki, ha nem Waiting sz�vegnek kellene lennie
            // De mivel a Waiting sz�veg RPC-vel j�n, itt egyszer�en fel�l�rjuk, ha van Timer
            GameHUD.Instance.UpdateTimer($"{colorHex}{prefix}{Mathf.CeilToInt(newVal)}</color>");
        }
        else
        {
            // Ha a Timer 0, k�t eset van:
            // 1. Lobbyban vagyunk �s v�runk -> Ezt az UpdateWaitingUIClientRpc kezeli, NE t�r�lj�k
            // 2. HunterRelease v�ge (InGame eleje) -> Ezt t�r�lni KELL.

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