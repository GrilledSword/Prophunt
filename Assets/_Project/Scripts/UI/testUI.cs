using Unity.Netcode;
using UnityEngine;

public class testUI : MonoBehaviour
{
    public void Start()
    {
        NetworkGameManager.Instance.StartGameServerRpc();
    }
    public void Host()
    {
        NetworkManager.Singleton.StartHost();
    }

    public void Join()
    {
        NetworkManager.Singleton.StartClient();
    }
}
