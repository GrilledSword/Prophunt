using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private TMP_InputField ipInputField;

    [Header("Scene Settings")]
    [SerializeField] private string gameSceneName = "GameScene";

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        quitButton.onClick.AddListener(OnQuitClicked);

        if (ipInputField != null) ipInputField.text = "127.0.0.1";
    }

    private void OnHostClicked()
    {
        bool success = NetworkManager.Singleton.StartHost();

        if (success)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else { }
    }

    private void OnJoinClicked()
    {
        if (ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
        {
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = ipInputField.text;
            }
        }
        NetworkManager.Singleton.StartClient();
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