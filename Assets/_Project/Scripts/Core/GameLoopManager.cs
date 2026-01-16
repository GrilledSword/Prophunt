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
        currentTimer.OnValueChanged += OnTimerChanged;

        // [JAVÍTÁS] Ha betöltött a pálya (és létrejött ez a script), 
        // azonnal reseteljük a UI-t LOKÁLISAN. Nem kell RPC!
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.ResetWinScreen();
        }
        if (lobbyUI != null) lobbyUI.SetActive(true);
        if (timerText != null) timerText.gameObject.SetActive(true); // Biztos látszódjon
    }
    private void OnSceneLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;

        isMatchStarted = false;
        isLobbyTimerRunning = false;
        isReleaseTimerRunning = false;

        // Spawn pontok keresése (Find)
        GameObject lobbyObj = GameObject.Find("LobbySpawnPoint");
        if (lobbyObj != null) lobbySpawnPoint = lobbyObj.transform;

        gameSpawnPoints.Clear();
        GameObject spawnsRoot = GameObject.Find("SpawnPoints");
        if (spawnsRoot != null)
        {
            foreach (Transform child in spawnsRoot.transform) gameSpawnPoints.Add(child);
        }

        // Teleport
        if (lobbySpawnPoint != null)
        {
            foreach (ulong clientId in clientsCompleted)
            {
                TeleportPlayer(clientId, lobbySpawnPoint.position);
            }
        }

        // A NetworkGameManager ResetLobby-ja majd intézi a Player resetet
    }
    private void Update()
    {
        if (!IsServer) return;

        if (isSceneReloaded && IsSpawned)
        {
            isSceneReloaded = false;
            // Biztos, ami biztos: Mindenkinek reseteljük a UI-t (de csak ha már spawnoltunk!)
            ResetUIClientRpc();

            // ÉS A LEGFONTOSABB:
            // A NetworkGameManager állapotát is vissza kell állítani LOBBY-ra!
            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.Instance.currentGameState.Value = NetworkGameManager.GameState.Lobby;
            }
        }
        // --- LOBBY FÁZIS ---
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
        // --- HUNTER RELEASE FÁZIS (IN GAME ELEJE) ---
        else if (isReleaseTimerRunning)
        {
            currentTimer.Value -= Time.deltaTime;
            if (currentTimer.Value <= 0f)
            {
                isReleaseTimerRunning = false;
                // Vége a bújócskának
                NetworkGameManager.Instance.SetHunterFree();
                //ToggleTimerTextClientRpc(false); // Eltüntetjük az órát
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

        // Idõzítõ beállítása
        isReleaseTimerRunning = true;
        currentTimer.Value = hunterReleaseTime;

        // [JAVÍTÁS] Kényszerítjük a megjelenést mindenkinél!
        ShowTimerUIClientRpc();
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
        if (GameHUD.Instance == null) return;

        if (newVal > 0)
        {
            string prefix = isMatchStarted ? "RELEASE: " : "START: ";
            GameHUD.Instance.UpdateTimer($"{prefix}{Mathf.CeilToInt(newVal)}");
        }
        else
        {
            GameHUD.Instance.UpdateTimer(""); // Eltüntetjük
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
    private void UpdateTimerUI(float previous, float current)
    {
        if (timerText != null)
        {
            // Ha a TimerText inaktív lenne, kapcsoljuk be (kliens oldali biztositek)
            if (!timerText.gameObject.activeInHierarchy && current > 0)
            {
                timerText.gameObject.SetActive(true);
            }

            if (current > 0)
            {
                // Ha MatchStarted van, de még ReleaseTimer fut -> RELEASE
                // Ha nem MatchStarted -> START
                string prefix = isMatchStarted ? "RELEASE: " : "START: ";
                timerText.text = $"{prefix}{Mathf.CeilToInt(current)}";

                // Extra színkód: Release alatt legyen PIROS a szöveg
                if (isMatchStarted) timerText.color = Color.red;
                else timerText.color = Color.white;
            }
            else
            {
                timerText.text = "";
            }
        }
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