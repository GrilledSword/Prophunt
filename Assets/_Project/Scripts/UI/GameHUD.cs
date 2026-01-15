using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    [Header("Általános UI")]
    [SerializeField] private Slider healthBar;
    [SerializeField] private Image healthFill;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("Vadász UI")]
    [SerializeField] private GameObject crosshair;

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
        if (crosshair != null) crosshair.SetActive(isHunter);

        if (healthFill != null)
        {
            healthFill.color = isHunter ? hunterHealthColor : deerHealthColor;
        }
    }

    public void UpdateHealth(float currentHealth)
    {
        // Debug, hogy lássuk, kap-e adatot
        // Debug.Log($"HUD Update: {currentHealth}"); 

        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        if (hpText != null)
        {
            // Egész számmá kerekítve írjuk ki
            hpText.text = $"{Mathf.CeilToInt(currentHealth)} HP";
        }
    }
}