using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private TMP_InputField playerCountInput;

    [Header("Match Settings UI")]
    [SerializeField] private TMP_InputField winsInput; // Hány nyerés kell?
    [SerializeField] private Toggle randomMapToggle;

    // Lista a konkrét map toggle-ökhöz (Inspectorból töltsd fel!)
    [SerializeField] private List<Toggle> specificMapToggles;
    // Fontos: A Toggle neve (GameObject neve) legyen ugyanaz, mint a Scene neve! 
    // Vagy csinálhatunk külön osztályt, de ez a legegyszerûbb trükk.

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

        // Default értékek
        if (playerCountInput != null) playerCountInput.text = "2";
        if (ipInputField != null) ipInputField.text = "127.0.0.1";
        if (winsInput != null) winsInput.text = "3";
        if (randomMapToggle != null) randomMapToggle.isOn = true;

        // --- TOGGLE LOGIKA BEKÖTÉSE ---

        // 1. Ha a Random Map-et nyomjuk meg
        if (randomMapToggle != null)
        {
            randomMapToggle.onValueChanged.AddListener((isOn) =>
            {
                if (isOn)
                {
                    // Minden mást kapcsoljunk ki
                    foreach (var t in specificMapToggles) t.SetIsOnWithoutNotify(false);
                }
            });
        }

        // 2. Ha bármelyik konkrét Map-et nyomjuk meg
        foreach (var toggle in specificMapToggles)
        {
            toggle.onValueChanged.AddListener((isOn) =>
            {
                if (isOn)
                {
                    // Randomot kapcsoljuk ki
                    if (randomMapToggle != null) randomMapToggle.SetIsOnWithoutNotify(false);

                    // A többi specifikusat is ki kell kapcsolni (hogy csak 1 maradhasson)
                    foreach (var other in specificMapToggles)
                    {
                        if (other != toggle) other.SetIsOnWithoutNotify(false);
                    }
                }
                else
                {
                    // Ha kikapcsoljuk az utolsó konkrétot is, akkor legyen alapból Random?
                    // Opcionális:
                    bool anyOn = false;
                    foreach (var t in specificMapToggles) if (t.isOn) anyOn = true;
                    if (!anyOn && randomMapToggle != null) randomMapToggle.isOn = true;
                }
            });
        }
    }

    private void OnHostClicked()
    {
        if (GameSessionSettings.Instance != null)
        {
            // Játékosszám
            if (playerCountInput != null && int.TryParse(playerCountInput.text, out int count))
                GameSessionSettings.Instance.MinPlayersToStart = Mathf.Max(1, count);

            // [ÚJ] Nyerések száma
            if (winsInput != null && int.TryParse(winsInput.text, out int wins))
                GameSessionSettings.Instance.TargetWins = Mathf.Max(1, wins);

            // [ÚJ] Map választás
            GameSessionSettings.Instance.IsRandomMap = randomMapToggle != null && randomMapToggle.isOn;

            if (!GameSessionSettings.Instance.IsRandomMap)
            {
                // Megkeressük melyik van bepipálva
                foreach (var t in specificMapToggles)
                {
                    if (t.isOn)
                    {
                        // TRÜKK: A Toggle GameObject nevét használjuk Scene névként!
                        // Pl. A toggle neve legyen "ForestMap" az Inspectorban.
                        GameSessionSettings.Instance.SelectedMapName = t.gameObject.name;
                        break;
                    }
                }
            }
            else
            {
                // Ha random, sorsolunk egyet indításnak
                GameSessionSettings.Instance.SelectedMapName = GameSessionSettings.Instance.GetRandomMap();
            }

            GameSessionSettings.Instance.ResetScores();
            GameSessionSettings.Instance.ShouldStartAsHost = true;

            // A kiválasztott pályát töltjük be!
            SceneManager.LoadScene(GameSessionSettings.Instance.SelectedMapName);
        }
    }

    private void OnJoinClicked()
    {
        if (GameSessionSettings.Instance != null)
        {
            GameSessionSettings.Instance.ShouldStartAsHost = false;
            if (ipInputField != null) GameSessionSettings.Instance.TargetIPAddress = ipInputField.text;
        }

        // Kliensnek mindegy melyik scene, a NetworkManager szinkronizálja (elvileg)
        // De jobb ha a "GameScene"-be vagy a Lobby-ba megy, aztán a szerver áthúzza.
        // Egyszerûség kedvéért töltsük be a RandomMap-et vagy egy LoadingScene-t.
        // Itt most feltételezzük, hogy a "GameScene" a default.
        SceneManager.LoadScene("GameScene");
    }

    private void OnQuitClicked() { Application.Quit(); }
}