using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem; // Kell az Input Systemhez!
using TMPro; // Ha TextMeshPro-t használsz (javasolt!)
using System.Collections;

namespace Prophunt.UI
{
    public class SplashScreenManager : MonoBehaviour
    {
        [Header("Beállítások")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private float blinkSpeed = 1.0f;

        [Header("UI Referenciák")]
        [SerializeField] private TextMeshProUGUI pressEnterText; // Húzd be ide a feliratot

        private bool isLoading = false;

        private void Start()
        {
            // Elindítjuk a villogó szöveg coroutine-t
            if (pressEnterText != null)
            {
                StartCoroutine(BlinkTextRoutine());
            }
        }

        private void Update()
        {
            // Ha már töltünk, ne csináljon semmit
            if (isLoading) return;

            // Figyeljük az ENTER gombot az Input Systemmel
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                LoadMainMenu();
            }

            // Opcionális: Érintõképernyõre (hogy mobilon is át lehessen lépni)
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                LoadMainMenu();
            }
        }

        private void LoadMainMenu()
        {
            isLoading = true;
            Debug.Log("Váltás a fõmenüre...");

            // Itt késõbb lehetne egy Fade Out animációt hívni
            SceneManager.LoadScene(mainMenuSceneName);
        }

        // Egy kis vizuális extra: villogó szöveg
        private IEnumerator BlinkTextRoutine()
        {
            while (!isLoading)
            {
                float alpha = (Mathf.Sin(Time.time * blinkSpeed) + 1.0f) / 2.0f;
                pressEnterText.color = new Color(pressEnterText.color.r, pressEnterText.color.g, pressEnterText.color.b, alpha);
                yield return null;
            }
        }
    }
}