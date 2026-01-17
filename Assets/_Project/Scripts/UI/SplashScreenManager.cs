using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections;

namespace Prophunt.UI
{
    public class SplashScreenManager : MonoBehaviour
    {
        [Header("Beállítások")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private float blinkSpeed = 1.0f;

        [Header("UI Referenciák")]
        [SerializeField] private TextMeshProUGUI pressEnterText;

        private bool isLoading = false;

        private void Start()
        {
            if (pressEnterText != null)
            {
                StartCoroutine(BlinkTextRoutine());
            }
        }

        private void Update()
        {
            if (isLoading) return;

            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                LoadMainMenu();
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                LoadMainMenu();
            }
        }

        private void LoadMainMenu()
        {
            isLoading = true;
            SceneManager.LoadScene(mainMenuSceneName);
        }

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