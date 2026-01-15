using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(HealthComponent))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Mozgás")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 9f;
    [SerializeField] private float lookSpeed = 2f;
    [SerializeField] private float hunterSprintCost = 5f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Dash (Csak Szarvas)")]
    [SerializeField] private float dashForce = 20f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 3f;

    [Header("References")]
    [SerializeField] private GameObject hunterModel;
    [SerializeField] private GameObject deerModel;
    [SerializeField] private Transform fpsMount;
    [SerializeField] private Transform tpsMount;

    public NetworkVariable<bool> isHunter = new NetworkVariable<bool>(false);

    private CharacterController characterController;
    private HealthComponent healthComponent;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private Vector3 velocity;
    private Camera sceneCamera;
    private float xRotation = 0f;

    private bool isDashing;
    private float dashEndTime;
    private float lastDashTime;

    public override void OnNetworkSpawn()
    {
        characterController = GetComponent<CharacterController>();
        healthComponent = GetComponent<HealthComponent>();

        if (IsServer)
        {
            // Beállítjuk a HealthComponent-nek is, hogy ki õ, hogy tudja kezelni a sérülést
            healthComponent.isHunter = isHunter.Value;
        }

        if (IsOwner)
        {
            sceneCamera = Camera.main;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        isHunter.OnValueChanged += OnRoleChanged;
        UpdateVisuals(isHunter.Value);
    }

    private void OnRoleChanged(bool previous, bool current)
    {
        UpdateVisuals(current);

        // [ÚJ] Ha játék közben változik a szerep (ritka, de lehetséges), frissítsük a UI-t
        if (IsOwner && GameHUD.Instance != null)
        {
            GameHUD.Instance.SetRoleUI(current);
        }
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
            // WASD
            Vector2 input = Keyboard.current.wKey.ReadValue() * Vector2.up +
                            Keyboard.current.sKey.ReadValue() * Vector2.down +
                            Keyboard.current.aKey.ReadValue() * Vector2.left +
                            Keyboard.current.dKey.ReadValue() * Vector2.right;
            moveInput = input.normalized;

            // DASH (Left Alt vagy Ctrl) - Csak Szarvas
            if (!isHunter.Value && Keyboard.current.leftAltKey.wasPressedThisFrame)
            {
                TryDash();
            }
        }

        if (Mouse.current != null) lookInput = Mouse.current.delta.ReadValue();
    }
    private void TryDash()
    {
        if (Time.time > lastDashTime + dashCooldown)
        {
            isDashing = true;
            dashEndTime = Time.time + dashDuration;
            lastDashTime = Time.time;

            // Itt jöhetne egy Dash hang vagy particle effekt
        }
    }

    private void Move()
    {
        if (characterController == null) return;

        // 1. DASH LOGIKA
        if (isDashing)
        {
            if (Time.time < dashEndTime)
            {
                Vector3 dashDir = transform.forward;
                if (moveInput.magnitude > 0)
                {
                    dashDir = transform.right * moveInput.x + transform.forward * moveInput.y;
                }
                characterController.Move(dashDir * dashForce * Time.deltaTime);
                return;
            }
            else
            {
                isDashing = false;
            }
        }

        // 2. SPRINT LOGIKA
        bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        float currentSpeed = walkSpeed;

        if (isSprinting && moveInput.magnitude > 0)
        {
            if (isHunter.Value)
            {
                if (healthComponent.currentHealth.Value > 5f)
                {
                    currentSpeed = sprintSpeed;
                    ApplySprintCostServerRpc();
                }
            }
            else
            {
                currentSpeed = sprintSpeed;
            }
        }

        // 3. MOZGÁS
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        characterController.Move(move * currentSpeed * Time.deltaTime);

        // Gravitáció
        if (characterController.isGrounded && velocity.y < 0) velocity.y = -2f;
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
    [ServerRpc]
    private void ApplySprintCostServerRpc()
    {
        // Csak a vadásztól vonunk le
        if (isHunter.Value)
        {
            healthComponent.ModifyHealth(-hunterSprintCost * Time.deltaTime);
        }
    }
}