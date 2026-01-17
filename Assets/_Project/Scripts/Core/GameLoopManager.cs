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
    [SerializeField] private float hunterReleaseTime = 15f;
    [SerializeField] private int minPlayersToStart = 2;

    [Header("UI")]
    [SerializeField] private GameObject lobbyUI;

    private Transform lobbySpawnPoint;
    private Transform deerSpawnPoint;
    private Transform hunterCabinSpawnPoint;
    private Transform hunterReleaseSpawnPoint;

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

        if (GameHUD.Instance != null) GameHUD.Instance.ResetWinScreen();
        if (lobbyUI != null) lobbyUI.SetActive(true);
    }
    private void OnSceneLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;

        isMatchStarted = false;
        isLobbyTimerRunning = false;
        isReleaseTimerRunning = false;

        // [ÚJ] Megkeressük a 4 pontot név alapján
        GameObject lobbyObj = GameObject.Find("LobbySpawnPoint");
        if (lobbyObj) lobbySpawnPoint = lobbyObj.transform;

        GameObject deerObj = GameObject.Find("DeerSpawnPoint");
        if (deerObj) deerSpawnPoint = deerObj.transform;

        GameObject cabinObj = GameObject.Find("HunterCabinSpawnPoint");
        if (cabinObj) hunterCabinSpawnPoint = cabinObj.transform;

        GameObject releaseObj = GameObject.Find("HunterReleaseSpawnPoint");
        if (releaseObj) hunterReleaseSpawnPoint = releaseObj.transform;

        // Mindenki Lobby
        if (lobbySpawnPoint != null)
        {
            foreach (ulong clientId in clientsCompleted)
            {
                TeleportPlayer(clientId, lobbySpawnPoint.position);
            }
        }

        isSceneReloaded = true;
    }
    private void Update()
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

                // [ÚJ] Vadász teleportálása a szabadba!
                MoveHunterToOutside();

                NetworkGameManager.Instance.SetHunterFree();
            }
        }
    }
    private void StartMatchSequence()
    {
        isLobbyTimerRunning = false;
        isMatchStarted = true;

        NetworkGameManager.Instance.StartGameServerRpc();
        DistributePlayersToSpawnPoints(); // Mindenki a helyére (Szarvas->Erdõ, Vadász->Kunyhó)
        ToggleLobbyUIClientRpc(false);

        isReleaseTimerRunning = true;
        currentTimer.Value = hunterReleaseTime;
    }
    private void DistributePlayersToSpawnPoints()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
            if (playerScript == null) continue;

            Vector3 targetPos;

            if (playerScript.isHunter.Value)
            {
                // Vadász -> Kunyhó (Cabin)
                if (hunterCabinSpawnPoint != null) targetPos = hunterCabinSpawnPoint.position;
                else targetPos = Vector3.zero;
            }
            else
            {
                // Szarvas -> Erdõ (DeerSpawnPoint + kis szórás, hogy ne egymásban legyenek)
                if (deerSpawnPoint != null)
                {
                    Vector3 randomOffset = new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
                    targetPos = deerSpawnPoint.position + randomOffset;
                }
                else targetPos = Vector3.zero;
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
        else GameHUD.Instance.UpdateTimer("");
    }
    private void MoveHunterToOutside()
    {
        if (hunterReleaseSpawnPoint == null) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var playerScript = client.PlayerObject.GetComponent<PlayerNetworkController>();
            if (playerScript != null && playerScript.isHunter.Value)
            {
                TeleportPlayer(client.ClientId, hunterReleaseSpawnPoint.position);
                Debug.Log("[GameLoop] Vadász kiengedve a szabadba!");
            }
        }
    }
    [ClientRpc] private void ToggleLobbyUIClientRpc(bool isActive) { if (lobbyUI != null) lobbyUI.SetActive(isActive); }
    [ClientRpc] private void ResetUIClientRpc() { if (GameHUD.Instance != null) GameHUD.Instance.ResetWinScreen(); if (lobbyUI != null) lobbyUI.SetActive(true); }
}