using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public class GameLoopManager : NetworkBehaviour
{
    public static GameLoopManager Instance { get; private set; }

    [Header("Idõzítés")]
    [SerializeField] private float lobbyTime = 10f;
    [SerializeField] private float hunterReleaseTime = 15f; // ÚJ: Mennyi ideig vak a vadász?
    [SerializeField] private int minPlayersToStart = 2; // Teszthez állítsd 1-re Inspectorban!

    [Header("Spawn Points (Tag-el keressük inkább!)")]
    private Transform lobbySpawnPoint;
    private List<Transform> gameSpawnPoints = new List<Transform>();

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject lobbyUI;

    private NetworkVariable<float> currentTimer = new NetworkVariable<float>(0f);
    private bool isLobbyTimerRunning = false;
    private bool isReleaseTimerRunning = false;
    private bool isMatchStarted = false;
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
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }

        // [FONTOS] Feliratkozás az idõzítõre
        currentTimer.OnValueChanged += OnTimerChanged;

        // [JAVÍTÁS] Lokális UI reset (nem RPC, mert spawnoláskor fut)
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.ResetWinScreen();
        }
        if (lobbyUI != null) lobbyUI.SetActive(true);
    }
    private void OnSceneLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;

        isMatchStarted = false;
        isLobbyTimerRunning = false;
        isReleaseTimerRunning = false;

        // Újrakeresés, mert a referenciák elvesztek
        GameObject lobbyObj = GameObject.Find("LobbySpawnPoint");
        if (lobbyObj != null) lobbySpawnPoint = lobbyObj.transform;

        gameSpawnPoints.Clear();
        GameObject spawnsRoot = GameObject.Find("SpawnPoints");
        if (spawnsRoot != null)
        {
            foreach (Transform child in spawnsRoot.transform) gameSpawnPoints.Add(child);
        }

        if (lobbySpawnPoint != null)
        {
            foreach (ulong clientId in clientsCompleted)
            {
                TeleportPlayer(clientId, lobbySpawnPoint.position);
            }
        }

        // Jelezzük az Update-nek, hogy volt reset
        isSceneReloaded = true;
    }
    private void Update()
    {
        if (!IsServer) return;

        // [JAVÍTÁS] Restart kezelés a következõ frame-ben
        if (isSceneReloaded && IsSpawned)
        {
            isSceneReloaded = false;

            // Itt már biztonságos az RPC
            ResetUIClientRpc();

            if (NetworkGameManager.Instance != null)
            {
                // Kényszerítjük a Lobby állapotot
                NetworkGameManager.Instance.currentGameState.Value = NetworkGameManager.GameState.Lobby;
            }
        }

        // --- LOBBY TIMER ---
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
                if (currentTimer.Value <= 0f)
                {
                    StartMatchSequence();
                }
            }
        }
        // --- RELEASE TIMER ---
        else if (isReleaseTimerRunning)
        {
            currentTimer.Value -= Time.deltaTime;
            if (currentTimer.Value <= 0f)
            {
                isReleaseTimerRunning = false;
                NetworkGameManager.Instance.SetHunterFree(); // Mehet a menet!
                // Nem rejtjük el a timert ClientRpc-vel, elég ha az értéke 0 lesz, az OnTimerChanged elintézi
            }
        }
    }
    private void StartMatchSequence()
    {
        isLobbyTimerRunning = false;
        isMatchStarted = true;

        NetworkGameManager.Instance.StartGameServerRpc();
        DistributePlayersToSpawnPoints();
        ToggleLobbyUIClientRpc(false);

        // Hunter Release indítása
        isReleaseTimerRunning = true;
        currentTimer.Value = hunterReleaseTime;
    }
    private void DistributePlayersToSpawnPoints()
    {
        if (gameSpawnPoints.Count == 0) return;
        int deerSpawnIndex = Random.Range(0, gameSpawnPoints.Count);
        Vector3 deerBasePosition = gameSpawnPoints[deerSpawnIndex].position;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
            if (playerScript == null) continue;

            Vector3 targetPos;
            if (!playerScript.isHunter.Value)
            {
                Vector3 randomOffset = new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));
                targetPos = deerBasePosition + randomOffset;
            }
            else
            {
                int hunterSpawnIndex = Random.Range(0, gameSpawnPoints.Count);
                targetPos = gameSpawnPoints[hunterSpawnIndex].position;
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
        // Ha nincs HUD, nincs mit frissíteni
        if (GameHUD.Instance == null) return;

        if (newVal > 0)
        {
            // Eldöntjük, mit írjunk ki
            string prefix = isMatchStarted ? "RELEASE: " : "START: ";
            GameHUD.Instance.UpdateTimer($"{prefix}{Mathf.CeilToInt(newVal)}");
        }
        else
        {
            // Ha 0 vagy kevesebb, töröljük a szöveget
            GameHUD.Instance.UpdateTimer("");
        }
    }
    [ClientRpc]
    private void ToggleLobbyUIClientRpc(bool isActive)
    {
        if (lobbyUI != null) lobbyUI.SetActive(isActive);
    }
    [ClientRpc]
    private void ResetUIClientRpc()
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.ResetWinScreen(); // Eltünteti a gyõzelmi feliratot
        }
        if (lobbyUI != null) lobbyUI.SetActive(true); // Lobby UI visszajön
    }
    [ClientRpc]
    private void ShowTimerUIClientRpc()
    {
        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
            timerText.text = "PREPARE TO HIDE!"; // Kezdõ szöveg amíg a timer szinkronizál
        }
    }
}