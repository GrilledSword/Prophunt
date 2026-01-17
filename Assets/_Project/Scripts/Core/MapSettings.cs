using UnityEngine;

public class MapSettings : MonoBehaviour
{
    public static MapSettings Instance { get; private set; }

    [Header("Spawn Pontok")]
    public Transform LobbySpawnPoint;
    public Transform DeerSpawnPoint;
    public Transform HunterCabinSpawnPoint;
    public Transform HunterReleaseSpawnPoint;

    [Header("Pálya Adatok")]
    public string mapName = "Forest";
    // Ide jöhet késõbb: nappal/éjszaka, idõjárás, stb.

    private void Awake()
    {
        // Minden pályabetöltésnél frissül az Instance az aktuális pályára
        Instance = this;
    }
}