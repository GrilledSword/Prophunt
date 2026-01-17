using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(HealthComponent))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Mozgás Beállítások")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 9f;
    [SerializeField] private float hunterSprintCost = 5f;
    [SerializeField] private float rotationSmoothTime = 0.1f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Dash (Csak Szarvas)")]
    [SerializeField] private float dashForce = 20f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 3f;

    [Header("Kamera Beállítások")]
    [SerializeField] private float mouseSensitivity = 2.0f;
    [SerializeField] private float tpsCameraDistance = 4.0f;
    [SerializeField] private Vector2 pitchLimits = new Vector2(-70f, 80f);

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

    private float cameraPitch = 0f;
    private float cameraYaw = 0f;
    private float currentRotationVelocity;

    private bool isDashing;
    private float dashEndTime;
    private float lastDashTime;

    private bool isGhost = false;
    private bool isUIConnected = false;

    private bool isTrapped = false;
    private int trapEscapePressesNeeded = 10;
    private int currentTrapPresses = 0;

    private FoodItem nearbyFoodItem = null;


    public override void OnNetworkSpawn()
    {
        characterController = GetComponent<CharacterController>();
        healthComponent = GetComponent<HealthComponent>();

        if (healthComponent != null)
        {
            healthComponent.isHunter = isHunter.Value;
        }

        if (IsServer)
        {
            if (NetworkGameManager.Instance != null)
                NetworkGameManager.Instance.RegisterPlayer(OwnerClientId, this);
        }

        if (IsOwner)
        {
            sceneCamera = Camera.main;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cameraYaw = transform.rotation.eulerAngles.y;

            TryConnectToHUD();
        }

        isHunter.OnValueChanged += OnRoleChanged;
        UpdateVisuals(isHunter.Value);
    }
    private void TryConnectToHUD()
    {
        if (isUIConnected) return;

        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.SetRoleUI(isHunter.Value);

            if (healthComponent != null)
            {
                GameHUD.Instance.UpdateMyHealth(healthComponent.currentHealth.Value);
                healthComponent.currentHealth.OnValueChanged += OnHealthChanged;
            }
            isUIConnected = true;
        }
    }
    private void OnHealthChanged(float previous, float current)
    {
        if (GameHUD.Instance != null)
        {
            GameHUD.Instance.UpdateMyHealth(current);
        }
    }
    public override void OnNetworkDespawn()
    {
        if (IsOwner && healthComponent != null)
        {
            healthComponent.currentHealth.OnValueChanged -= OnHealthChanged;
        }
        base.OnNetworkDespawn();
    }
    private void OnRoleChanged(bool previous, bool current)
    {
        if (healthComponent != null)
        {
            healthComponent.isHunter = current;
        }

        UpdateVisuals(current);
        if (IsOwner && GameHUD.Instance != null) GameHUD.Instance.SetRoleUI(current);
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
        if (!isUIConnected) TryConnectToHUD();

        if (isGhost)
        {
            HandleInput();
            MoveGhost();
            return;
        }
        if (isTrapped) { HandleTrapEscape(); return; }

        HandleInteractionInput();
        HandleInput();
        if (isHunter.Value) { MoveHunter(); LookHunter(); }
        else { MoveDeer(); LookDeer(); }
    }
    private void LateUpdate()
    {
        if (!IsOwner) return;
        if (sceneCamera == null) sceneCamera = Camera.main;
        if (sceneCamera != null) UpdateCameraPosition();
    }
    private void UpdateCameraPosition()
    {
        Transform targetMount = isHunter.Value ? fpsMount : tpsMount;
        if (targetMount == null) return;

        if (isHunter.Value)
        {
            sceneCamera.transform.position = targetMount.position;
            sceneCamera.transform.rotation = targetMount.rotation;
        }
        else
        {
            Vector3 targetPos = targetMount.position - (targetMount.forward * tpsCameraDistance);
            sceneCamera.transform.position = targetPos;
            sceneCamera.transform.rotation = targetMount.rotation;
        }
    }
    private void HandleInput()
    {
        if (Keyboard.current != null)
        {
            Vector2 input = Keyboard.current.wKey.ReadValue() * Vector2.up +
                            Keyboard.current.sKey.ReadValue() * Vector2.down +
                            Keyboard.current.aKey.ReadValue() * Vector2.left +
                            Keyboard.current.dKey.ReadValue() * Vector2.right;
            moveInput = input.normalized;

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
        }
    }
    private void MoveHunter()
    {
        if (!characterController.enabled) return;
        ApplyGravity();

        float speed = GetCurrentSpeed();
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        characterController.Move(move * speed * Time.deltaTime);
    }
    private void MoveDeer()
    {
        if (!characterController.enabled) return;
        ApplyGravity();

        if (isDashing)
        {
            if (Time.time < dashEndTime)
            {
                Vector3 dashDir = transform.forward;
                if (moveInput.magnitude > 0 && sceneCamera != null)
                {
                    float targetAngle = Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg + sceneCamera.transform.eulerAngles.y;
                    dashDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
                }
                characterController.Move(dashDir * dashForce * Time.deltaTime);
                return;
            }
            else isDashing = false;
        }
        if (moveInput.magnitude >= 0.1f && sceneCamera != null)
        {
            float targetAngle = Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg + sceneCamera.transform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref currentRotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            characterController.Move(moveDir.normalized * GetCurrentSpeed() * Time.deltaTime);
        }
    }
    private float GetCurrentSpeed()
    {
        bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        float speed = walkSpeed;

        if (isSprinting && moveInput.magnitude > 0)
        {
            if (isHunter.Value)
            {
                if (healthComponent.currentHealth.Value > 5f)
                {
                    speed = sprintSpeed;
                    ApplySprintCostServerRpc();
                }
            }
            else speed = sprintSpeed;
        }
        return speed;
    }
    private void ApplyGravity()
    {
        if (characterController.isGrounded && velocity.y < 0) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }
    private void LookHunter()
    {
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime * 10f;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime * 10f;

        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -90f, 90f);
        transform.Rotate(Vector3.up * mouseX);

        if (fpsMount != null)
            fpsMount.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }
    private void LookDeer()
    {
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime * 10f;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime * 10f;

        cameraYaw += mouseX;
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, pitchLimits.x, pitchLimits.y);

        if (tpsMount != null)
        {
            tpsMount.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        }
    }
    private void MoveGhost()
    {
        if (moveInput.magnitude < 0.1f && !Keyboard.current.spaceKey.isPressed && !Keyboard.current.leftCtrlKey.isPressed)
        {
            return;
        }

        LookHunter();

        float speed = 15f;
        Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y);

        if (Keyboard.current.spaceKey.isPressed) moveDir.y = 1;
        if (Keyboard.current.leftCtrlKey.isPressed) moveDir.y = -1;

        transform.Translate(moveDir * speed * Time.deltaTime);
    }
    public void SetNearbyFood(FoodItem food)
    {
        nearbyFoodItem = food;
        if (GameHUD.Instance != null)
        {
            if (food != null)
            {
                GameHUD.Instance.SetInteractionText(true, "PRESS 'E' TO EAT");
            }
            else
            {
                GameHUD.Instance.SetInteractionText(false);
            }
        }
    }
    private void HandleInteractionInput()
    {
        if (nearbyFoodItem != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            // Megettük! Küldjük a kérést a szervernek
            EatFoodServerRpc(nearbyFoodItem.NetworkObjectId);

            // Lokálisan azonnal eltüntetjük a feliratot, hogy ne nyomjuk meg 2x
            SetNearbyFood(null);
        }
    }


    [ServerRpc]
    private void ApplySprintCostServerRpc()
    {
        if (isHunter.Value && healthComponent.isDraining.Value)
        {
            healthComponent.ModifyHealth(-hunterSprintCost * Time.deltaTime);
        }
    }
    [ServerRpc]
    private void EatFoodServerRpc(ulong foodNetworkId)
    {
        // Megkeressük az objektumot ID alapján
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(foodNetworkId, out var netObj))
        {
            var food = netObj.GetComponent<FoodItem>();
            if (food != null)
            {
                food.Eat(healthComponent);
            }
        }
    }
    [ServerRpc]
    private void RequestUntrapServerRpc()
    {
        SetTrappedClientRpc(false);
    }



    [ClientRpc]
    public void SetGhostModeClientRpc()
    {
        if (!IsOwner) return;

        Debug.Log("Spectator Mód Aktiválva!");
        isGhost = true;

        if (characterController != null) characterController.enabled = false;

        if (hunterModel) hunterModel.SetActive(false);
        if (deerModel) deerModel.SetActive(false);

        // GameHUD.Instance.ShowSpectatorUI();
    }
    [ClientRpc]
    public void TransformToPanicModeClientRpc()
    {
        var shootingSystem = GetComponent<HunterShootingSystem>();
        if (shootingSystem != null) shootingSystem.enabled = false;

        if (IsOwner)
        {
            hunterModel.SetActive(false);
            deerModel.SetActive(true);
        }
        else
        {
            hunterModel.SetActive(false);
            deerModel.SetActive(true);
        }
        hunterSprintCost = 0f;
    }
    private void HandleTrapEscape()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            currentTrapPresses++;
            Debug.Log($"Szabadulás: {currentTrapPresses}/{trapEscapePressesNeeded}");

            if (currentTrapPresses >= trapEscapePressesNeeded)
            {
                RequestUntrapServerRpc();
            }
        }
    }
    [ClientRpc]
    public void SetTrappedClientRpc(bool trapped)
    {
        isTrapped = trapped;
        if (trapped)
        {
            currentTrapPresses = 0;
        }
        else { }
    }
    [ClientRpc]
    public void ResetPlayerStateClientRpc()
    {
        isGhost = false;
        isTrapped = false;
        isDashing = false;

        var shooting = GetComponent<HunterShootingSystem>();
        if (shooting != null) shooting.ResetShootingState();

        if (characterController != null) characterController.enabled = true;

        gravity = -9.81f;
        walkSpeed = 5f;
        sprintSpeed = 9f;

        UpdateVisuals(false);

        if (IsOwner && GameHUD.Instance != null)
        {
            GameHUD.Instance.SetRoleUI(false);
        }
    }
}