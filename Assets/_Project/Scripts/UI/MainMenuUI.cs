using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // Kell a szövegdobozokhoz (pl. IP cím)

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TMP_InputField ipInputField; // Ha IP-t akarnánk írni (opcionális)

    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "GameScene"; // A pálya neve

    private void Start()
    {
        // Feliratkozunk a gombokra. Profibb, mint a Unity Editorban húzogatni.
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        quitButton.onClick.AddListener(OnQuitClicked);

        // Alapértelmezett IP (Localhost), hogy ne kelljen mindig beírni teszthez
        if (ipInputField != null) ipInputField.text = "127.0.0.1";
    }

    private void OnHostClicked()
    {
        Debug.Log("Host indítása...");

        // 1. Elindítjuk a Host-ot a NetworkManageren
        bool success = NetworkManager.Singleton.StartHost();

        if (success)
        {
            Debug.Log("Host sikeres! Pálya betöltése...");
            // 2. A Netcode beépített SceneManagerét használjuk a váltáshoz!
            // Ez automatikusan viszi magával a klienseket is, ha majd csatlakoznak.
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else
        {
            Debug.LogError("Nem sikerült elindítani a Host-ot!");
        }
    }

    private void OnJoinClicked()
    {
        Debug.Log("Csatlakozás...");

        // Ha van IP mezõ, beállítjuk (Unity Transportot feltételezve)
        if (ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
        {
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = ipInputField.text;
            }
        }

        // Kliens indítása
        // Megjegyzés: A Scene váltást a szerver intézi, amint csatlakozunk, automatikusan átkerülünk.
        NetworkManager.Singleton.StartClient();

        // Itt kikapcsolhatnánk a menü UI-t, hogy látszódjon a "Connecting..."
        hostButton.interactable = false;
        joinButton.interactable = false;
    }

    private void OnQuitClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}