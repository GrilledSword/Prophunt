using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

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

    [Header("Színek")]
    [SerializeField] private Color hunterHealthColor = Color.red;
    [SerializeField] private Color deerHealthColor = new Color(0.4f, 0.8f, 0.2f);

    private bool amIHunter = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
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
    }
    public void ResetWinScreen()
    {
        if (winPanel != null) winPanel.SetActive(false);
        if (hunterInfoPanel != null) hunterInfoPanel.SetActive(false);
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
}