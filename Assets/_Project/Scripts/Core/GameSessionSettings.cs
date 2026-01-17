using UnityEngine;
using System.Collections.Generic;

public class GameSessionSettings : MonoBehaviour
{
    public static GameSessionSettings Instance { get; private set; }

    // Alap beállítások
    public int MinPlayersToStart { get; set; } = 2;
    public bool ShouldStartAsHost { get; set; } = false;
    public string TargetIPAddress { get; set; } = "127.0.0.1";

    // [ÚJ] Meccs beállítások
    public int TargetWins { get; set; } = 3; // Hány nyerésig menjen?
    public bool IsRandomMap { get; set; } = true;
    public string SelectedMapName { get; set; } = "ForestMap"; // Default

    public int CurrentHunterScore { get; set; } = 0;
    public int CurrentDeerScore { get; set; } = 0;

    // [ÚJ] Elérhetõ pályák listája (Scene nevek)
    // Ezt majd az Inspectorból töltsd fel!
    public List<string> availableMaps = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // Segéd: Random map választása
    public string GetRandomMap()
    {
        if (availableMaps.Count == 0) return "GameScene"; // Fallback
        return availableMaps[Random.Range(0, availableMaps.Count)];
    }
    public void ResetScores()
    {
        CurrentHunterScore = 0;
        CurrentDeerScore = 0;
    }
}