using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using static NetworkGameManager;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(HealthComponent))]
[RequireComponent(typeof(AudioSource))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Mozgás Beállítások")]
    [SerializeField] private float hunterWalkSpeed = 5f;
    [SerializeField] private float hunterSprintSpeed = 9f;
    [SerializeField] private float deerWalkSpeed = 6f;
    [SerializeField] private float deerSprintSpeed = 10f;

    [SerializeField] private float hunterSprintCost = 5f;
    [SerializeField] private float rotationSmoothTime = 0.1f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Dash (Csak Szarvas)")]
    [SerializeField] private float dashForce = 20f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 3f;

    [Header("Kamera Beállítások")]
    [SerializeField] private float mouseSensitivity = 0.5f;
    [SerializeField] private float tpsCameraDistance = 4.0f;
    [SerializeField] private Vector2 pitchLimits = new Vector2(-70f, 80f);

    [Header("Kamera Stabilizátor (FPS)")]
    [SerializeField] private bool useStabilizer = true; // Kapcsoló
    [SerializeField] private float posLerpSpeed = 15f;

    [Header("Harc Beállítások (Hunter)")]
    [SerializeField] private float reloadTime = 0.1f; // Mennyi ideig tart az új nyíl elõvétele
    [SerializeField] private float shootAnimDuration = 0.1f; // Mennyi idõ a lövés animáció

    [Header("Audio & Animáció")]
    private Animator animator;

    [SerializeField] private AudioClip eatSoundClip;
    [SerializeField] private AudioSource audioSource;

    [Header("References")]
    [SerializeField] private GameObject hunterModel;
    [SerializeField] private GameObject deerModel;
    [SerializeField] private Transform fpsMount;
    [SerializeField] private Transform tpsMount;

    public NetworkVariable<bool> isHunter = new NetworkVariable<bool>(false);

    private NetworkVariable<bool> isMimicEating = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
    private bool isPanicMode = false;

    // [ADDED] Új állapotjelzõk a mozgás korlátozásához
    private bool isAiming = false;
    private bool isReloading = false;

    private bool isTrapped = false;
    private int trapEscapePressesNeeded = 10;
    private int currentTrapPresses = 0;

    private FoodItem nearbyFoodItem = null;

    // Animator Hash ID-k
    private int animIDSpeed;
    private int animIDIsEating;
    private int animIDShoot;
    private int animIDReload;
    private int animIDAiming; // [ADDED]

    public override void OnNetworkSpawn()
    {
        characterController = GetComponent<CharacterController>();
        healthComponent = GetComponent<HealthComponent>();

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.spatialBlend = 1.0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.maxDistance = 20f;
        }

        // Animator ID-k
        animIDSpeed = Animator.StringToHash("Speed");
        animIDIsEating = Animator.StringToHash("IsEating");
        animIDShoot = Animator.StringToHash("Shoot");
        animIDReload = Animator.StringToHash("IsReloading");
        animIDAiming = Animator.StringToHash("IsAiming"); // [ADDED]

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
        isMimicEating.OnValueChanged += OnMimicEatingChanged;

        UpdateVisuals(isHunter.Value);
    }

    private void OnMimicEatingChanged(bool previous, bool current)
    {
        if (animator != null && !isHunter.Value)
        {
            animator.SetBool(animIDIsEating, current);
        }
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
        isMimicEating.OnValueChanged -= OnMimicEatingChanged;
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
            if (hunterModel) hunterModel.SetActive(hunterParams);
            if (deerModel) deerModel.SetActive(!hunterParams);
        }
        else
        {
            if (hunterModel) hunterModel.SetActive(hunterParams);
            if (deerModel) deerModel.SetActive(!hunterParams);
        }

        GameObject targetModel = hunterParams ? hunterModel : deerModel;

        if (targetModel != null)
        {
            animator = targetModel.GetComponentInChildren<Animator>(true);
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (!isUIConnected) TryConnectToHUD();

        if (isGhost) { HandleInput(); MoveGhost(); return; }
        if (isTrapped) { HandleTrapEscape(); return; }

        HandleInteractionInput();
        HandleInput(); // Mozgás input (WASD)

        if (isHunter.Value)
        {
            HandleHunterCombat(); // [ÚJ] Itt kezeljük a Célzást és Lövést
            MoveHunter();
            LookHunter();
        }
        else
        {
            HandleMimicEating();
            MoveDeer();
            LookDeer();
        }

        HandleAnimations();
    }
    private void HandleHunterCombat()
    {
        // 1. ÁLLAPOT ELLENÕRZÉS: Ha nem InGame, akkor STOP!
        // Ha nincs GameLoopManager, vagy nem InGame az állapot, akkor nem célozhatunk és nem lõhetünk.
        bool canCombat = true;
        if (NetworkGameManager.Instance != null)
        {
            if (NetworkGameManager.Instance.currentGameState.Value != GameState.InGame)
            {
                canCombat = false;
            }
        }

        // Ha tiltva van a harc (Lobby, Pánik, Vége), kényszerítsük ki a békés állapotot
        if (!canCombat)
        {
            if (isAiming)
            {
                isAiming = false;
                if (animator != null) animator.SetBool(animIDAiming, false);
            }
            return; // Kilépünk, így a lövés kód le se fut!
        }

        // 2. Normál harci logika (Célzás)
        if (Mouse.current != null && !isReloading)
        {
            bool rightClickHeld = Mouse.current.rightButton.isPressed;
            if (isAiming != rightClickHeld)
            {
                isAiming = rightClickHeld;
                if (animator != null) animator.SetBool(animIDAiming, isAiming);
            }
        }
        else if (isReloading)
        {
            isAiming = false;
            if (animator != null) animator.SetBool(animIDAiming, false);
        }

        // 3. Lövés (Csak ha InGame voltunk, és eljutottunk idáig)
        if (isAiming && !isReloading && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            PerformShoot();
        }
    }
    private void PerformShoot()
    {
        // Most már csak akkor jutunk ide, ha InGame van!

        // 1. Lövés a rendszeren keresztül
        var shootingSystem = GetComponent<HunterShootingSystem>();
        if (shootingSystem != null)
        {
            // Mivel már ellenõriztük a GameState-et, itt biztosan lõhetünk
            // (A HunterShootingSystem-ben lévõ safety check maradhat, nem baj)
            shootingSystem.TryShoot();
        }

        // 2. Animáció
        if (animator != null) animator.SetTrigger(animIDShoot);

        // 3. Újratöltés indítása
        StartCoroutine(ReloadSequence());
    }
    private IEnumerator ReloadSequence()
    {
        // Várunk, amíg a lövés animáció lemegy (pl. 0.5 sec)
        yield return new WaitForSeconds(shootAnimDuration);

        // Töltés kezdete
        isReloading = true;
        SetReloadAnim(true);

        // Várunk a töltés idejére (pl. 2.0 sec)
        yield return new WaitForSeconds(reloadTime);

        // Töltés vége
        isReloading = false;
        SetReloadAnim(false);
    }
    private void HandleAnimations()
    {
        if (animator == null) return;

        float currentSpeed = new Vector3(characterController.velocity.x, 0, characterController.velocity.z).magnitude;
        float animSpeed = 0f;

        if (isHunter.Value)
        {
            if (currentSpeed > 0.1f)
            {
                // Ha célzunk, akkor Séta (1), ha nem és sprintelünk, akkor Futás (2)
                if (isAiming) animSpeed = 1f;
                else animSpeed = (currentSpeed > hunterWalkSpeed + 1f) ? 2f : 1f;
            }
        }
        else
        {
            if (currentSpeed > 0.1f) animSpeed = (currentSpeed > deerWalkSpeed + 1f) ? 2f : 1f;
        }

        animator.SetFloat(animIDSpeed, Mathf.Lerp(animator.GetFloat(animIDSpeed), animSpeed, Time.deltaTime * 10f));
    }

    // --- Publikus hívások a ShootingSystem-nek ---

    public void TriggerShootAnim()
    {
        if (animator != null && isHunter.Value)
        {
            animator.SetTrigger(animIDShoot);
        }
    }

    // [MODIFIED] Újratöltés beállítása és mozgás tiltásának kezelése
    public void SetReloadAnim(bool reloading)
    {
        isReloading = reloading; // Ez blokkolja majd a mozgást a GetCurrentSpeed-ben

        if (animator != null && isHunter.Value)
        {
            animator.SetBool(animIDReload, reloading);
        }
    }

    // [ADDED] Célzás beállítása
    public void SetAimingAnim(bool aiming)
    {
        isAiming = aiming; // Ez tiltja a sprintet

        if (animator != null && isHunter.Value)
        {
            animator.SetBool(animIDAiming, aiming);
        }
    }
    // ---------------------------------------------

    private void HandleMimicEating()
    {
        if (isHunter.Value) return;

        bool isHoldingE = Keyboard.current.eKey.isPressed;

        if (isHoldingE != isMimicEating.Value)
        {
            SetMimicEatingServerRpc(isHoldingE);
        }
    }

    [ServerRpc]
    private void SetMimicEatingServerRpc(bool isEating)
    {
        isMimicEating.Value = isEating;
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;
        if (sceneCamera == null) sceneCamera = Camera.main;
        if (sceneCamera != null) UpdateCameraPosition();
    }
    private void UpdateCameraPosition()
    {
        bool useFps = isHunter.Value && !isPanicMode;
        Transform targetMount = useFps ? fpsMount : tpsMount;

        if (targetMount == null) return;

        if (useFps)
        {
            // FPS MÓD (Itt kell a stabilizátor)
            if (useStabilizer)
            {
                // 1. Pozíció: Lerp-eljük, hogy a kis rázkódásokat kisimítsuk
                sceneCamera.transform.position = Vector3.Lerp(
                    sceneCamera.transform.position,
                    targetMount.position,
                    Time.deltaTime * posLerpSpeed
                );

                // 2. Rotáció: EZ A TITOK!
                // NEM vesszük át a targetMount.rotation-t (mert az a fejcsonttal együtt rázkódik).
                // Helyette a tiszta Input alapú rotációt használjuk (CameraPitch + Test Yaw).
                // Így a fej mozoghat alattunk, de a kamera stabil marad, mint egy igazi FPS-ben.
                Quaternion stableRotation = Quaternion.Euler(cameraPitch, transform.eulerAngles.y, 0f);
                sceneCamera.transform.rotation = stableRotation;
            }
            else
            {
                // Régi, "kemény" kötés (rázkódós)
                sceneCamera.transform.position = targetMount.position;
                sceneCamera.transform.rotation = targetMount.rotation;
            }
        }
        else
        {
            // TPS MÓD (Szarvas / Pánik) - Itt maradhat a régi logika
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

        // Ha a sebesség 0 (pl. töltés miatt), a move vektor is nullázódik
        characterController.Move(move * speed * Time.deltaTime);
    }
    private void MoveDeer()
    {
        if (!characterController.enabled) return;
        ApplyGravity();

        if (isMimicEating.Value) return;

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

    // [MODIFIED] Itt történik a varázslat: Sebesség korlátozás állapotok szerint
    private float GetCurrentSpeed()
    {
        if (isPanicMode) return hunterSprintSpeed;

        // 1. Ha töltünk, nincs mozgás!
        if (isHunter.Value && isReloading) return 0f;

        bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        if (isHunter.Value)
        {
            // 2. Ha célzunk, akkor NINCS sprint, csak séta!
            if (isAiming) return hunterWalkSpeed;

            // 3. Egyébként lehet sprintelni
            if (isSprinting && moveInput.magnitude > 0 && healthComponent.currentHealth.Value > 5f)
            {
                ApplySprintCostServerRpc();
                return hunterSprintSpeed;
            }
            return hunterWalkSpeed;
        }
        else
        {
            return isSprinting ? deerSprintSpeed : deerWalkSpeed;
        }
    }
    private void ApplyGravity()
    {
        if (characterController.isGrounded && velocity.y < 0) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void LookHunter()
    {
        float mouseX = lookInput.x * mouseSensitivity * 0.1f;
        float mouseY = lookInput.y * mouseSensitivity * 0.1f;

        cameraPitch -= mouseY;
        Vector2 limits = isPanicMode ? pitchLimits : new Vector2(-90f, 90f);
        cameraPitch = Mathf.Clamp(cameraPitch, limits.x, limits.y);

        transform.Rotate(Vector3.up * mouseX);

        // Ez forgatja a fegyvert/kart a modellen (vizuális), de a kamera már a "Matek"-ot követi fentrõl
        if (fpsMount != null && !isPanicMode)
            fpsMount.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);

        if (tpsMount != null && isPanicMode)
            tpsMount.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    private void LookDeer()
    {
        float mouseX = lookInput.x * mouseSensitivity * 0.1f;
        float mouseY = lookInput.y * mouseSensitivity * 0.1f;

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
            EatFoodServerRpc(nearbyFoodItem.NetworkObjectId);
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
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(foodNetworkId, out var netObj))
        {
            var food = netObj.GetComponent<FoodItem>();
            if (food != null)
            {
                food.Eat(healthComponent);
                PlayEatSoundClientRpc(transform.position);
            }
        }
    }

    [ClientRpc]
    private void PlayEatSoundClientRpc(Vector3 pos)
    {
        if (eatSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(eatSoundClip);
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
    }

    [ClientRpc]
    public void TransformToPanicModeClientRpc()
    {
        var shootingSystem = GetComponent<HunterShootingSystem>();
        if (shootingSystem != null) shootingSystem.enabled = false;

        isPanicMode = true;
        UpdateVisuals(isHunter.Value);

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
        isPanicMode = false;

        // [ADDED] Resetnél ezeket is nullázzuk
        isAiming = false;
        isReloading = false;

        var shooting = GetComponent<HunterShootingSystem>();
        if (shooting != null) shooting.ResetShootingState();

        if (characterController != null) characterController.enabled = true;

        gravity = -9.81f;

        UpdateVisuals(isHunter.Value);

        if (IsOwner && GameHUD.Instance != null)
        {
            GameHUD.Instance.SetRoleUI(isHunter.Value);
        }
    }
}