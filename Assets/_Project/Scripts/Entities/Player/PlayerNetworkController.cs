using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Beállítások")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lookSpeed = 2f; // Mouse sensitivity
    [SerializeField] private float gravity = -9.81f;

    [Header("References")]
    [SerializeField] private GameObject hunterModel;
    [SerializeField] private GameObject deerModel;

    // [DELETED] Régi kamera megoldás törölve
    // [SerializeField] private Transform cameraRoot;   
    // [SerializeField] private Vector3 fpsOffset...
    // [SerializeField] private Vector3 tpsOffset...

    // [MODIFIED] Új Camera Mount referenciák
    [Header("Camera Mounts")]
    [SerializeField] private Transform fpsMount; // Húzd ide az FPS_Mount objectet
    [SerializeField] private Transform tpsMount; // Húzd ide a TPS_Mount objectet

    public NetworkVariable<bool> isHunter = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private CharacterController characterController;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private Vector3 velocity;

    // [MODIFIED] Nem tároljuk a kamerát, csak keressük
    private Camera sceneCamera;
    private float xRotation = 0f;

    public override void OnNetworkSpawn()
    {
        characterController = GetComponent<CharacterController>();

        if (IsServer)
        {
            NetworkGameManager.Instance.RegisterPlayer(OwnerClientId, this);
        }

        if (IsOwner)
        {
            // [MODIFIED] Megkeressük a Scene-ben lévõ egyetlen kamerát
            sceneCamera = Camera.main;
            if (sceneCamera == null) Debug.LogError("Nincs MainCamera a Scene-ben!");

            // Egér elrejtése
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        isHunter.OnValueChanged += OnRoleChanged;
        UpdateVisuals(isHunter.Value);
    }

    private void OnRoleChanged(bool previous, bool current)
    {
        UpdateVisuals(current);
    }

    private void UpdateVisuals(bool hunterParams)
    {
        if (IsOwner)
        {
            // [MODIFIED] Saját nézet: A modellt elrejtjük FPS-ben, hogy ne lógjon a kamerába
            // De TPS-ben (szarvas) látni akarjuk magunkat
            hunterModel.SetActive(false); // Vadászként csak a kezed (külön fegyver modell) látszik majd
            deerModel.SetActive(!hunterParams);
        }
        else
        {
            // Mások látják a modellt
            hunterModel.SetActive(hunterParams);
            deerModel.SetActive(!hunterParams);
        }

        // [DELETED] A kamera szülõjének átállítása törölve innen, Update-ben kezeljük a követést simábban
    }

    [ServerRpc]
    public void SetRoleServerRpc(bool _isHunter)
    {
        isHunter.Value = _isHunter;
    }

    private void Update()
    {
        if (!IsOwner) return;

        HandleInput();
        Move();
        Look();

        // [NEW] Kamera követés logika minden frame-ben
        UpdateCameraPosition();
    }

    // [NEW] Ez az új metódus kezeli a kamera "rácsatolását" a Mount pontokra
    private void UpdateCameraPosition()
    {
        if (sceneCamera == null) return;

        Transform targetMount = isHunter.Value ? fpsMount : tpsMount;

        // Pozíció és forgás másolása
        // Tipp: Késõbb ide lehet tenni simítást (Lerp) a profibb hatásért TPS-ben
        sceneCamera.transform.position = targetMount.position;
        sceneCamera.transform.rotation = targetMount.rotation;
    }

    private void HandleInput()
    {
        // ... (Ez a rész változatlan a mockup inputtal, amíg nem állítjuk be az Input Systemet)
        if (Keyboard.current != null)
        {
            float x = 0; float y = 0;
            if (Keyboard.current.wKey.isPressed) y = 1;
            if (Keyboard.current.sKey.isPressed) y = -1;
            if (Keyboard.current.aKey.isPressed) x = -1;
            if (Keyboard.current.dKey.isPressed) x = 1;
            moveInput = new Vector2(x, y);
        }

        if (Mouse.current != null)
        {
            lookInput = Mouse.current.delta.ReadValue();
        }
    }

    private void Move()
    {
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        characterController.Move(move * moveSpeed * Time.deltaTime);

        if (characterController.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void Look()
    {
        float mouseX = lookInput.x * lookSpeed * Time.deltaTime;
        float mouseY = lookInput.y * lookSpeed * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        // [MODIFIED] Most már a Mount pontokat forgatjuk, nem közvetlenül a kamerát
        // A Hunter (FPS) nézetnél a Mount együtt forog a játékossal horizontálisan, de vertikálisan külön

        // A player testét forgatjuk jobbra-balra
        transform.Rotate(Vector3.up * mouseX);

        // A Mount pontok bólintása (fel-le nézés)
        // Ezt lokálisan kell kezelni a Mountokon
        fpsMount.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // A TPS mountnak lehet, hogy kell egy Pivot pont, hogy a karakter körül forogjon
        // De MVP-nek (Minimum Viable Product) most elég, ha õ is bólint
        tpsMount.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}