using UnityEngine;
using Unity.Netcode;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Lejátszók (Audio Sources)")]
    [SerializeField] private AudioSource musicSource;    // Zene (Változik)
    [SerializeField] private AudioSource ambienceSource; // Háttérzaj (Folyamatos)

    [Header("Zenék (Music)")]
    [SerializeField] private AudioClip lobbyMusic;
    [SerializeField] private AudioClip hunterReleaseMusic;
    [SerializeField] private AudioClip inGameMusic; // Ha nincs zene InGame, ezt hagyd üresen az Inspectorban!
    [SerializeField] private AudioClip hunterPanicMusic;
    [SerializeField] private AudioClip gameOverMusic;

    [Header("Atmoszféra (Ambience)")]
    [SerializeField] private AudioClip gameAmbience; // Erdõ hang, szél, stb.

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }
    private void Start()
    {
        // Alapból Lobby zene + Ambience
        PlayMusic(lobbyMusic);
        PlayAmbience(true);
    }
    private void Update()
    {
        // Figyeljük a GameState változást
        if (NetworkGameManager.Instance != null)
        {
            var state = NetworkGameManager.Instance.currentGameState.Value;
            HandleGameStateAudio(state);
        }
    }

    private NetworkGameManager.GameState lastState = NetworkGameManager.GameState.Lobby;
    private void HandleGameStateAudio(NetworkGameManager.GameState currentState)
    {
        if (currentState == lastState) return;

        switch (currentState)
        {
            case NetworkGameManager.GameState.Lobby:
                PlayMusic(lobbyMusic);
                PlayAmbience(true); // Ambience BE
                break;

            case NetworkGameManager.GameState.HunterRelease:
                PlayMusic(hunterReleaseMusic);
                PlayAmbience(true); // Ambience MARAD
                break;

            case NetworkGameManager.GameState.InGame:
                PlayMusic(inGameMusic);
                PlayAmbience(true); // Ambience MARAD
                break;

            case NetworkGameManager.GameState.HunterPanic:
                PlayMusic(hunterPanicMusic);
                PlayAmbience(false); // Ambience KI (Csak a horror zene menjen)
                break;

            case NetworkGameManager.GameState.GameOver:
                PlayMusic(gameOverMusic);
                PlayAmbience(false); // Ambience KI
                break;
        }

        lastState = currentState;
    }

    private void PlayMusic(AudioClip clip)
    {
        if (musicSource == null) return;

        // Ha nincs megadva clip (pl. InGame csend legyen a zene sávon), akkor stop
        if (clip == null)
        {
            musicSource.Stop();
            return;
        }

        // Ha már ez szól, ne indítsa újra!
        if (musicSource.clip == clip && musicSource.isPlaying) return;

        musicSource.Stop();
        musicSource.clip = clip;
        musicSource.Play();
    }
    private void PlayAmbience(bool play)
    {
        if (ambienceSource == null || gameAmbience == null) return;

        if (play)
        {
            // Csak akkor indítjuk el, ha még nem szól a megfelelõ clip
            if (!ambienceSource.isPlaying || ambienceSource.clip != gameAmbience)
            {
                ambienceSource.clip = gameAmbience;
                ambienceSource.loop = true; // Biztos ami biztos
                ambienceSource.Play();
            }
        }
        else
        {
            // Leállítás
            if (ambienceSource.isPlaying)
            {
                ambienceSource.Stop();
            }
        }
    }
}