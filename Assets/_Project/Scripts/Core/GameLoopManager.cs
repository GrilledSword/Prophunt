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
    [SerializeField] private int minPlayersToStart = 2; // Ezt állítsd 1-re az Inspectorban a teszthez!

    [Header("Spawn Referenciák")]
    [SerializeField] private Transform lobbySpawnPoint;
    [SerializeField] private List<Transform> gameSpawnPoints;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject lobbyUI;

    private NetworkVariable<float> currentTimer = new NetworkVariable<float>(0f);
    private bool isTimerRunning = false;

    // [NEW] Ez a változó a "Retesz". Ha igaz, akkor már nem lobbyzunk.
    private bool isMatchStarted = false;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }
        currentTimer.OnValueChanged += UpdateTimerUI;
    }

    private void OnSceneLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsServer) return;
        foreach (ulong clientId in clientsCompleted)
        {
            TeleportPlayer(clientId, lobbySpawnPoint.position);
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        // [NEW] Ha a meccs már elindult, akkor a Lobby logika INAKTÍV.
        // Így elkerüljük a végtelen újraindítást.
        if (isMatchStarted) return;

        int playerCount = NetworkManager.Singleton.ConnectedClientsList.Count;

        if (playerCount >= minPlayersToStart && !isTimerRunning)
        {
            StartCountdown();
        }
        else if (playerCount < minPlayersToStart && isTimerRunning)
        {
            StopCountdown();
        }

        if (isTimerRunning)
        {
            currentTimer.Value -= Time.deltaTime;
            if (currentTimer.Value <= 0f)
            {
                StartMatch();
            }
        }
    }

    private void StartCountdown()
    {
        isTimerRunning = true;
        currentTimer.Value = lobbyTime;
        Debug.Log("Visszaszámlálás indul!");
    }

    private void StopCountdown()
    {
        isTimerRunning = false;
        currentTimer.Value = 0f;
    }

    private void StartMatch()
    {
        isTimerRunning = false;

        // [NEW] Bekapcsoljuk a reteszt: A meccs elindult!
        isMatchStarted = true;

        Debug.Log("MECCS INDUL! Sorsolás és Teleport.");

        NetworkGameManager.Instance.StartGameServerRpc();
        DistributePlayersToSpawnPoints();
        ToggleLobbyUIClientRpc(false);
    }

    private void DistributePlayersToSpawnPoints()
    {
        // [BIZTONSÁG] Ha nincs spawn pont, ne omoljon össze
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

    private void UpdateTimerUI(float previous, float current)
    {
        if (timerText != null)
        {
            if (current > 0)
                timerText.text = $"START IN: {Mathf.CeilToInt(current)}";
            else
                timerText.text = ""; // Ha vége, üres legyen
        }
    }

    [ClientRpc]
    private void ToggleLobbyUIClientRpc(bool isActive)
    {
        if (lobbyUI != null) lobbyUI.SetActive(isActive);
    }
}