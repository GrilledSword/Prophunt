using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Beállítások")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lookSpeed = 2f;
    [SerializeField] private float gravity = -9.81f;

    [Header("References")]
    [SerializeField] private GameObject hunterModel;
    [SerializeField] private GameObject deerModel;

    [Header("Camera Mounts")]
    [SerializeField] private Transform fpsMount;
    [SerializeField] private Transform tpsMount;

    public NetworkVariable<bool> isHunter = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private CharacterController characterController;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private Vector3 velocity;

    private Camera sceneCamera;
    private float xRotation = 0f;

    public override void OnNetworkSpawn()
    {
        characterController = GetComponent<CharacterController>();

        // 1. Regisztráció
        if (IsServer && NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.RegisterPlayer(OwnerClientId, this);
        }

        // 2. Csak a saját karakterünkkel foglalkozunk
        if (IsOwner)
        {
            // --- DETEKTÍV MÓD START ---
            Debug.Log($"[PLAYER DEBUG] Spawnolás... Owner ClientID: {OwnerClientId}");

            sceneCamera = Camera.main;

            // Ha a Camera.main nem találja, megpróbáljuk máshogy
            if (sceneCamera == null)
            {
                Debug.LogWarning("[PLAYER DEBUG] FIGYELEM! Camera.main értéke NULL! Megpróbálom megkeresni FindObjectOfType-pal...");
                sceneCamera = FindObjectOfType<Camera>();
            }

            if (sceneCamera != null)
            {
                Debug.Log($"[PLAYER DEBUG] Siker! Kamera megtalálva: {sceneCamera.name}");

                // Egér elrejtése
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Debug.LogError("[PLAYER DEBUG] KRITIKUS HIBA: Még mindig nincs kamera! Ellenõrizd a 'MainCamera' taget a Scene-ben!");
            }

            // Mount pontok ellenõrzése
            if (fpsMount == null) Debug.LogError("[PLAYER DEBUG] HIBA: Az 'Fps Mount' nincs behúzva az Inspectorban!");
            if (tpsMount == null) Debug.LogError("[PLAYER DEBUG] HIBA: A 'Tps Mount' nincs behúzva az Inspectorban!");
            // --- DETEKTÍV MÓD END ---
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
            if (hunterModel) hunterModel.SetActive(false);
            if (deerModel) deerModel.SetActive(!hunterParams);
        }
        else
        {
            if (hunterModel) hunterModel.SetActive(hunterParams);
            if (deerModel) deerModel.SetActive(!hunterParams);
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        HandleInput();
        Move();
        Look();
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        // [JAVÍTÁS] Ha a kamera elveszett (pl. scene váltásnál törlõdött a menü kamera),
        // akkor megpróbáljuk megkeresni az újat a GameScene-ben.
        if (sceneCamera == null)
        {
            sceneCamera = Camera.main;
        }

        // Csak akkor frissítünk, ha VAN érvényes kamera
        if (sceneCamera != null)
        {
            UpdateCameraPosition();
        }
    }

    private void UpdateCameraPosition()
    {
        // Itt már tudjuk, hogy a sceneCamera nem null (mert a LateUpdate ellenõrizte)

        Transform targetMount = isHunter.Value ? fpsMount : tpsMount;

        if (targetMount != null)
        {
            sceneCamera.transform.position = targetMount.position;
            sceneCamera.transform.rotation = targetMount.rotation;
        }
    }

    private void HandleInput()
    {
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
        if (characterController == null) return;

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

        transform.Rotate(Vector3.up * mouseX);

        if (fpsMount != null) fpsMount.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        if (tpsMount != null) tpsMount.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }
}