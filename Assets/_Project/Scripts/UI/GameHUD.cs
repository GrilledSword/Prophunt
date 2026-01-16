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
    [SerializeField] private GameObject hunterInfoPanel; // Szarvasoknak
    [SerializeField] private Slider hunterSanityBar;     // Szarvasoknak
    [SerializeField] private TextMeshProUGUI hunterSanityText;

    [Header("Egyéb UI")]
    [SerializeField] private GameObject crosshair;
    [SerializeField] private TextMeshProUGUI timerText; // KÖZÉPEN FENT
    [SerializeField] private GameObject winPanel;
    [SerializeField] private TextMeshProUGUI winText;

    [Header("Színek")]
    [SerializeField] private Color hunterHealthColor = Color.red;
    [SerializeField] private Color deerHealthColor = new Color(0.4f, 0.8f, 0.2f);

    private void Awake()
    {
        // Singleton biztosítása
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Debug.Log("GameHUD Inicializálva!"); // Debug, hogy lássuk, él-e
    }
    public void SetRoleUI(bool isHunter)
    {
        // 1. Saját nézet
        if (crosshair != null) crosshair.SetActive(isHunter);
        if (myHealthFill != null) myHealthFill.color = isHunter ? hunterHealthColor : deerHealthColor;

        // 2. Hunter Info Panel (Csak Szarvasok látják a publikus adatot)
        if (hunterInfoPanel != null)
        {
            hunterInfoPanel.SetActive(!isHunter);
        }

        // Reset Win Panel
        if (winPanel != null) winPanel.SetActive(false);
    }
    public void UpdateMyHealth(float currentHealth)
    {
        if (myHealthBar != null) myHealthBar.value = currentHealth;
        if (myHpText != null) myHpText.text = $"{Mathf.CeilToInt(currentHealth)}";
    }
    public void UpdateHunterSanity(float sanity)
    {
        if (hunterSanityBar != null) hunterSanityBar.value = sanity;
        if (hunterSanityText != null) hunterSanityText.text = $"HUNTER SANITY: {Mathf.CeilToInt(sanity)}%";
    }
    public void UpdateTimer(string text)
    {
        if (timerText != null) timerText.text = text;
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
    }
}