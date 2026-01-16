using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    public enum GameState { Lobby, InGame, HunterPanic, GameOver }
    private NetworkVariable<GameState> currentGameState = new NetworkVariable<GameState>(GameState.Lobby);

    private Dictionary<ulong, PlayerNetworkController> connectedPlayers = new Dictionary<ulong, PlayerNetworkController>();

    private List<SafeZone> allSafeZones = new List<SafeZone>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
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
        currentGameState.Value = GameState.InGame;

        // Hunter sorsolás (régi kód) ...
        List<ulong> clientIds = connectedPlayers.Keys.ToList();
        if (clientIds.Count == 0) return;
        ulong hunterId = clientIds[Random.Range(0, clientIds.Count)];

        foreach (var player in connectedPlayers)
        {
            bool isHunter = (player.Key == hunterId);
            player.Value.isHunter.Value = isHunter;

            // [ÚJ] Kényszerítjük a HealthComponentet is a Szerveren
            var health = player.Value.GetComponent<HealthComponent>();
            if (health != null) health.isHunter = isHunter;

            Debug.Log($"[Server] {player.Key} szerepe: {(isHunter ? "HUNTER" : "DEER")}");
        }
    }
    public void OnPlayerDied(ulong victimId, bool wasHunter, bool isInstaKill)
    {
        if (!IsServer) return;

        if (wasHunter)
        {
            if (isInstaKill)
            {
                // TAPOSÓAKNA: Nincs kegyelem, nincs Pánik Mód.
                Debug.Log("VADÁSZ FELROBBANT! (InstaKill) -> Szarvasok nyertek.");
                EndGameServerRpc(false); // False = Hunter Lost (Deer Won)

                // Opcionális: Spectatorba tehetjük, hogy nézze a végét
                if (connectedPlayers.TryGetValue(victimId, out var hunterScript))
                {
                    hunterScript.SetGhostModeClientRpc();
                }
            }
            else
            {
                // SIMA HALÁL (Sanity 0): Pánik Mód!
                Debug.Log("VADÁSZ LEESETT! PÁNIK MÓD INDUL!");
                TriggerHunterPanicMode(victimId);
            }
        }
        else
        {
            // SZARVAS HALÁL -> Spectator
            Debug.Log($"Szarvas ({victimId}) meghalt. Spectator mód.");
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

        // 1. Megkeressük a vadászt
        if (connectedPlayers.TryGetValue(hunterId, out var hunterScript))
        {
            // Átváltoztatjuk "Prédává" (vizuálisan szarvassá, fegyver elvétel)
            hunterScript.TransformToPanicModeClientRpc();

            // 2. Safe Zone kiválasztása (NEM A LEGKÖZELEBBI!)
            if (allSafeZones.Count > 0)
            {
                SafeZone selectedZone = null;

                if (allSafeZones.Count == 1)
                {
                    selectedZone = allSafeZones[0];
                }
                else
                {
                    // Rendezzük távolság szerint (Csökkenõ sorrend = legtávolabbi elõl)
                    var sortedZones = allSafeZones.OrderByDescending(z => Vector3.Distance(z.transform.position, hunterScript.transform.position)).ToList();

                    // Veszünk egyet a legtávolabbiak közül (pl. a felsõ 50%-ból random)
                    int safeIndex = Random.Range(0, Mathf.CeilToInt(sortedZones.Count / 2f));
                    selectedZone = sortedZones[safeIndex];
                }

                if (selectedZone != null)
                {
                    selectedZone.SetActive(true);
                    Debug.Log($"[PANIC] Kijelölt SafeZone: {selectedZone.name} a pozíción: {selectedZone.transform.position}");
                    // Itt lehetne ClientRpc-vel jelezni a Hunternek, hol a ház (Waypoint)
                }
                else
                {
                    Debug.LogError("[PANIC] HIBA! Nincs elérhetõ SafeZone a listában!");
                }
            }
        }
    }
    private void CheckDeerWinCondition()
    {
        // Megszámoljuk az élõ szarvasokat
        int livingDeer = 0;
        foreach (var p in connectedPlayers.Values)
        {
            // Ha nem hunter és van élete
            if (!p.isHunter.Value && p.GetComponent<HealthComponent>().currentHealth.Value > 0)
            {
                livingDeer++;
            }
        }

        if (livingDeer == 0)
        {
            EndGameServerRpc(true); // Hunter nyert (mindenki halott)
        }
    }
    [ServerRpc]
    public void EndGameServerRpc(bool hunterWon)
    {
        currentGameState.Value = GameState.GameOver;
        Debug.Log(hunterWon ? "VADÁSZ NYERT!" : "SZARVASOK NYERTEK!");
    }
    public bool IsHunterInPanic()
    {
        return currentGameState.Value == GameState.HunterPanic;
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
            allSafeZones = FindObjectsOfType<SafeZone>().ToList();
            foreach (var zone in allSafeZones) zone.SetActive(false); // Kikapcsoljuk õket
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
}