using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    [Header("Általános UI")]
    [SerializeField] private Slider healthBar;       // A csúszka
    [SerializeField] private Image healthFill;       // A csúszka színe (hogy változtathassuk)
    [SerializeField] private TextMeshProUGUI hpText; // Kiírjuk számmal is (pl. 100/100)

    [Header("Vadász UI")]
    [SerializeField] private GameObject crosshair;   // A célkereszt
    [SerializeField] private GameObject ammoPanel;   // (Késõbbre, ha lesz tár)

    [Header("Szarvas UI")]
    [SerializeField] private GameObject staminaBar;  // (Késõbbre, ha futni kell)

    [Header("Színek")]
    [SerializeField] private Color hunterHealthColor = Color.red;
    [SerializeField] private Color deerHealthColor = new Color(0.4f, 0.8f, 0.2f); // Szép zöld

    private void Awake()
    {
        // Singleton, hogy bárhonnan elérjük
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }

    // Ezt hívjuk meg, amikor eldõl, hogy kik vagyunk
    public void SetRoleUI(bool isHunter)
    {
        // 1. Célkereszt logika
        // Csak a vadásznak kell célkereszt!
        if (crosshair != null) crosshair.SetActive(isHunter);

        // 2. Színek beállítása
        if (healthFill != null)
        {
            healthFill.color = isHunter ? hunterHealthColor : deerHealthColor;
        }

        // 3. Reset
        UpdateHealth(100);
    }

    public void UpdateHealth(int currentHealth)
    {
        if (healthBar != null)
        {
            healthBar.value = currentHealth;
        }

        if (hpText != null)
        {
            hpText.text = $"{currentHealth} HP";
        }

        // Extra: Ha kevés az élet, pirosodjon (opcionális effekt)
    }
}