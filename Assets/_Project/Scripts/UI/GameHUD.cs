using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    [Header("Match Over UI")]
    [SerializeField] private GameObject matchOverPanel;
    [SerializeField] private TextMeshProUGUI matchOverWinnerText;
    [SerializeField] private GameObject hostControls;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button exitMatchButton;

    [Header("Pontszámok (Csak Számok!)")]
    [SerializeField] private TextMeshProUGUI hunterScoreText;
    [SerializeField] private TextMeshProUGUI deerScoreText;
    [SerializeField] private TextMeshProUGUI goalScoreText;

    [Header("Saját Állapot")]
    [SerializeField] private Slider myHealthBar;
    [SerializeField] private Image myHealthFill;
    [SerializeField] private TextMeshProUGUI myHpText;

    [Header("Hunter Publikus Infó (Mindenki látja)")]
    [SerializeField] private GameObject hunterInfoPanel;
    [SerializeField] private Slider hunterSanityBar;
    [SerializeField] private TextMeshProUGUI hunterSanityText;

    [Header("Egyéb UI")]
    [SerializeField] private GameObject crosshair;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TextMeshProUGUI winText;
    [SerializeField] private TextMeshProUGUI interactionText;

    [Header("Pause Menu")]
    [SerializeField] private GameObject pauseMenuPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button exitButton;

    [Header("Színek")]
    [SerializeField] private Color hunterHealthColor = Color.red;
    [SerializeField] private Color deerHealthColor = new Color(0.4f, 0.8f, 0.2f);

    private bool amIHunter = false;
    private bool isPaused = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    private void Start()
    {
        if (resumeButton != null) resumeButton.onClick.AddListener(TogglePauseMenu);
        if (exitButton != null) exitButton.onClick.AddListener(ExitToMainMenu);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);

        if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        if (exitMatchButton != null) exitMatchButton.onClick.AddListener(ExitToMainMenu);
        if (matchOverPanel != null) matchOverPanel.SetActive(false);
    }
    private void Update()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePauseMenu();
        }
    }
    public void TogglePauseMenu()
    {
        isPaused = !isPaused;

        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(isPaused);
        }

        if (isPaused)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
    public void ExitToMainMenu()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            Destroy(NetworkManager.Singleton.gameObject);
        }
        SceneManager.LoadScene("MainMenu");
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    public void SetRoleUI(bool isHunter)
    {
        amIHunter = isHunter;

        if (crosshair != null) crosshair.SetActive(isHunter);

        if (myHealthFill != null) myHealthFill.color = isHunter ? hunterHealthColor : deerHealthColor;

        if (hunterInfoPanel != null) hunterInfoPanel.SetActive(false);

        ResetWinScreen();
        SetInteractionText(false);
    }
    public void UpdateMyHealth(float currentHealth)
    {
        if (myHealthBar != null) myHealthBar.value = currentHealth;
        if (myHpText != null) myHpText.text = $"{Mathf.CeilToInt(currentHealth)}";
    }
    public void UpdateHunterSanity(float sanity)
    {
        if (amIHunter)
        {
            if (hunterInfoPanel != null) hunterInfoPanel.SetActive(false);
            return;
        }

        bool showPanel = false;
        if (NetworkGameManager.Instance != null)
        {
            showPanel = NetworkGameManager.Instance.IsInGame();
        }

        if (hunterInfoPanel != null)
        {
            if (hunterInfoPanel.activeSelf != showPanel)
            {
                hunterInfoPanel.SetActive(showPanel);
            }
        }

        if (showPanel)
        {
            if (hunterSanityBar != null) hunterSanityBar.value = sanity;
            if (hunterSanityText != null) hunterSanityText.text = $"HUNTER SANITY: {Mathf.CeilToInt(sanity)}%";
        }
    }
    public void UpdateTimer(string text)
    {
        if (timerText != null)
        {
            bool shouldBeActive = !string.IsNullOrEmpty(text);
            if (timerText.gameObject.activeSelf != shouldBeActive)
            {
                timerText.gameObject.SetActive(shouldBeActive);
            }
            timerText.text = text;
        }
    }
    public void ShowWinScreen(string text)
    {
        if (winPanel != null)
        {
            winPanel.SetActive(true);
            if (winText != null) winText.text = text;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    public void ResetWinScreen()
    {
        if (winPanel != null) winPanel.SetActive(false);
        if (hunterInfoPanel != null) hunterInfoPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    public void ShowNotification(string message)
    {
        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
            timerText.text = message;
            timerText.color = Color.red;

            StartCoroutine(HideNotificationRoutine());
        }
    }
    public void SetInteractionText(bool isActive, string text = "")
    {
        if (interactionText != null)
        {
            interactionText.gameObject.SetActive(isActive);
            if (isActive) interactionText.text = text;
        }
    }
    private System.Collections.IEnumerator HideNotificationRoutine()
    {
        yield return new WaitForSeconds(3f);
        if (timerText != null)
        {
            timerText.text = "";
            timerText.gameObject.SetActive(false);
            timerText.color = Color.white;
        }
    }
    public void UpdateScores(int hScore, int dScore, int target)
    {
        if (hunterScoreText != null)
            hunterScoreText.text = hScore.ToString();

        if (deerScoreText != null)
            deerScoreText.text = dScore.ToString();

        if (goalScoreText != null)
            goalScoreText.text = target.ToString();
    }
    public void ShowMatchOverScreen(string winnerTeam, bool isHost)
    {
        if (matchOverPanel != null)
        {
            matchOverPanel.SetActive(true);
            if (matchOverWinnerText != null)
                matchOverWinnerText.text = $"MATCH OVER!\nWINNER: {winnerTeam}";

            // Csak a Host lássa a gombokat
            if (hostControls != null)
                hostControls.SetActive(isHost);

            // Kurzor elõhozása
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
    private void OnContinueClicked()
    {
        // Csak a Host hívhatja ezt, de a gomb is csak neki látszik
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.LoadNextMap();
        }
    }
}