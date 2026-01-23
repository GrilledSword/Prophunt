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

    [Header("Animation Fix")]
    private Vector3 lastPosition;
    private float calculatedSpeed;
    private float animationVelocity;

    [SerializeField] private AudioClip eatSoundClip;
    [SerializeField] private AudioSource audioSource;

    [Header("References")]
    [SerializeField] private GameObject hunterModel;
    [SerializeField] private GameObject deerModel;
    [SerializeField] private Transform fpsMount;
    [SerializeField] private Transform tpsMount;

    public NetworkVariable<bool> isHunter = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isAimingNetworked = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
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

        animIDSpeed = Animator.StringToHash("Speed");
        animIDIsEating = Animator.StringToHash("IsEating");
        animIDShoot = Animator.StringToHash("Shoot");
        animIDReload = Animator.StringToHash("IsReloading");
        animIDAiming = Animator.StringToHash("IsAiming");

        if (healthComponent != null) healthComponent.isHunter = isHunter.Value;

        if (IsServer && NetworkGameManager.Instance != null)
            NetworkGameManager.Instance.RegisterPlayer(OwnerClientId, this);

        if (IsOwner)
        {
            sceneCamera = Camera.main;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            cameraYaw = transform.rotation.eulerAngles.y;
            TryConnectToHUD();
        }

        lastPosition = transform.position;

        isHunter.OnValueChanged += OnRoleChanged;
        isMimicEating.OnValueChanged += OnMimicEatingChanged;

        // [ÚJ] Figyeljük a változást, hogy azonnal frissüljön az animáció
        isAimingNetworked.OnValueChanged += OnAimingStateChanged;

        UpdateVisuals(isHunter.Value);
    }
    private void OnAimingStateChanged(bool previous, bool current)
    {
        if (animator != null && isHunter.Value)
        {
            animator.SetBool(animIDAiming, current);
        }
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
        // [JAVÍTÁS] Szétválasztottuk az Update logikát.
        // A HandleAnimations MINDENKINEK lefut, nem csak az Ownernek!

        if (IsOwner)
        {
            HandleOwnerLogic();
        }

        // [JAVÍTÁS] Animációt mindenki frissít (Owner és Proxy is)
        CalculateNetworkVelocity();
        HandleAnimations();
    }
    private void HandleOwnerLogic()
    {
        if (!isUIConnected) TryConnectToHUD();

        if (isGhost) { HandleInput(); MoveGhost(); return; }
        if (isTrapped) { HandleTrapEscape(); return; }

        HandleInteractionInput();
        HandleInput(); // Mozgás input (WASD)

        if (isHunter.Value)
        {
            HandleHunterCombat();
            MoveHunter();
            LookHunter();
        }
        else
        {
            HandleMimicEating();
            MoveDeer();
            LookDeer();
        }
    }
    private void CalculateNetworkVelocity()
    {
        if (IsOwner)
        {
            // Ownernél a CharacterController pontos értéket ad
            calculatedSpeed = new Vector3(characterController.velocity.x, 0, characterController.velocity.z).magnitude;
        }
        else
        {
            // Proxyknál (akiket a hálózat mozgat) a pozíció változásából számolunk
            Vector3 currentPos = transform.position;
            float dist = Vector3.Distance(new Vector3(currentPos.x, 0, currentPos.z), new Vector3(lastPosition.x, 0, lastPosition.z));

            // Ha a delta idõ nagyon kicsi, 0-val osztanánk, ezt kerüljük el
            if (Time.deltaTime > 0.0001f)
            {
                calculatedSpeed = dist / Time.deltaTime;
            }
            else
            {
                calculatedSpeed = 0f;
            }

            lastPosition = currentPos;
        }
    }
    private void HandleHunterCombat()
    {
        bool canCombat = true;

        // 1. Általános GameState ellenõrzés
        if (NetworkGameManager.Instance != null && NetworkGameManager.Instance.currentGameState.Value != GameState.InGame)
            canCombat = false;

        // 2. [JAVÍTÁS] Specifikus Pánik ellenõrzés
        // Ha Pánik mód van, akkor NINCS harc, pont.
        if (isPanicMode) canCombat = false;

        if (!canCombat)
        {
            // Fail-safe: Ha valahogy mégis TRUE maradt a hálózati változó (pl. lag miatt),
            // és mi vagyunk a tulajok, akkor kényszerítjük a FALSE-t.
            if (IsOwner && isAimingNetworked.Value)
            {
                SetAimingServerRpc(false);
            }
            return; // Itt azonnal kilépünk, így az Input le se fut!
        }

        // --- INNEN CSAK AKKOR FUT LE, HA MINDEN OKÉ (Nincs Pánik) ---

        if (Mouse.current != null && !isReloading)
        {
            bool rightClickHeld = Mouse.current.rightButton.isPressed;

            // Ha változott az input állapota, jelezzük a szervernek
            if (isAimingNetworked.Value != rightClickHeld)
            {
                SetAimingServerRpc(rightClickHeld);
            }
        }
        else if (isReloading && isAimingNetworked.Value)
        {
            // Ha töltünk, nem célozhatunk
            SetAimingServerRpc(false);
        }

        if (isAimingNetworked.Value && !isReloading && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            PerformShoot();
        }
    }
    private void PerformShoot()
    {
        var shootingSystem = GetComponent<HunterShootingSystem>();
        if (shootingSystem != null) shootingSystem.TryShoot();

        // A Shoot Trigger egy "esemény", azt RPC-vel küldjük át, hogy mindenki lejátssza
        TriggerShootAnimServerRpc();

        StartCoroutine(ReloadSequence());
    }
    private IEnumerator ReloadSequence()
    {
        yield return new WaitForSeconds(shootAnimDuration);

        isReloading = true;
        SetReloadAnimServerRpc(true);

        yield return new WaitForSeconds(reloadTime);

        isReloading = false;
        SetReloadAnimServerRpc(false);
    }
    private void HandleAnimations()
    {
        if (animator == null) return;

        float animSpeed = 0f;
        if (calculatedSpeed > 0.1f)
        {
            if (isHunter.Value)
            {
                // [JAVÍTÁS] Most már a hálózati változót nézzük!
                if (isAimingNetworked.Value) animSpeed = 1f;
                else animSpeed = (calculatedSpeed > hunterWalkSpeed + 1f) ? 2f : 1f;
            }
            else
            {
                animSpeed = (calculatedSpeed > deerWalkSpeed + 1f) ? 2f : 1f;
            }
        }
        animator.SetFloat(animIDSpeed, Mathf.Lerp(animator.GetFloat(animIDSpeed), animSpeed, Time.deltaTime * 10f));
    }
    public void TriggerShootAnim()
    {
        if (animator != null && isHunter.Value)
        {
            animator.SetTrigger(animIDShoot);
        }
    }
    public void SetReloadAnim(bool reloading)
    {
        isReloading = reloading; // Ez blokkolja majd a mozgást a GetCurrentSpeed-ben

        if (animator != null && isHunter.Value)
        {
            animator.SetBool(animIDReload, reloading);
        }
    }
    public void SetAimingAnim(bool aiming)
    {
        isAiming = aiming; // Ez tiltja a sprintet

        if (animator != null && isHunter.Value)
        {
            animator.SetBool(animIDAiming, aiming);
        }
    }
    private void HandleMimicEating()
    {
        if (isHunter.Value) return;

        bool isHoldingE = Keyboard.current.eKey.isPressed;

        if (isHoldingE != isMimicEating.Value)
        {
            SetMimicEatingServerRpc(isHoldingE);
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
    private float GetCurrentSpeed()
    {
        // 1. Pánik mód felülbírál mindent -> MAX SEBESSÉG
        // Fontos: Itt már nem érdekel minket, hogy céloz-e a játékos, mert a Pánik felülírja.
        if (isPanicMode) return hunterSprintSpeed;

        if (isHunter.Value && isReloading) return 0f;

        bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        if (isHunter.Value)
        {
            // Ha nincs pánik, és célzunk, akkor lassú séta
            if (isAimingNetworked.Value) return hunterWalkSpeed;

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


    [ServerRpc]
    private void SetMimicEatingServerRpc(bool isEating)
    {
        isMimicEating.Value = isEating;
    }
    [ServerRpc]
    private void SetAimingServerRpc(bool aiming)
    {
        isAimingNetworked.Value = aiming;
    }
    [ServerRpc]
    private void TriggerShootAnimServerRpc()
    {
        TriggerShootAnimClientRpc();
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
    [ServerRpc]
    private void SetReloadAnimServerRpc(bool reloading)
    {
        // Használhatnánk NetworkVariable-t is, de az animátor paraméter szinkronhoz most jó a ClientRpc is,
        // vagy frissíthetnénk egy NetworkVariable-t. Maradjunk az RPC-nél az egyszerûség kedvéért.
        SetReloadAnimClientRpc(reloading);
    }
    [ServerRpc]
    private void RequestUntrapServerRpc()
    {
        SetTrappedClientRpc(false);
    }


    [ClientRpc]
    private void PlayEatSoundClientRpc(Vector3 pos)
    {
        if (eatSoundClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(eatSoundClip);
        }
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
        hunterSprintCost = 0f;

        // [JAVÍTÁS] Kényszerített reset (Kill Switch)
        // Azonnal leállítjuk a helyi állapotokat
        isReloading = false;

        // Animátor takarítás azonnal (hogy ne maradjon vizuálisan beragadva)
        if (animator != null)
        {
            animator.SetBool(animIDAiming, false);
            animator.SetBool(animIDReload, false);
            // Opcionális: Ha van "Equip" vagy hasonló layer, azt is resetelni kellene
        }

        // Ha mi vagyunk a tulajdonosok, közölnünk kell a hálózattal is, hogy "befejeztem a célzást"
        // Ez oldja meg a beragadást, ha nyomva tartod a gombot
        if (IsOwner)
        {
            // Csak akkor küldünk RPC-t, ha a hálózati változó szerint még célzunk
            if (isAimingNetworked.Value)
            {
                SetAimingServerRpc(false);
            }
        }

        UpdateVisuals(isHunter.Value);

        Debug.Log("[Player] Panic Mode Activated - Combat Disabled & States Reset");
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

        isAiming = false;
        isReloading = false;

        var shooting = GetComponent<HunterShootingSystem>();
        if (shooting != null)
        {
            shooting.enabled = true;
            shooting.ResetShootingState();
        }

        if (characterController != null) characterController.enabled = true;

        gravity = -9.81f;
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
            animator.SetBool(animIDIsEating, false);
            animator.SetBool(animIDAiming, false);
            animator.SetBool(animIDReload, false);
            animator.SetFloat(animIDSpeed, 0f);
        }
        if (IsServer)
        {
            isMimicEating.Value = false;
            isAimingNetworked.Value = false;
        }

        UpdateVisuals(isHunter.Value);

        if (IsOwner && GameHUD.Instance != null)
        {
            GameHUD.Instance.SetRoleUI(isHunter.Value);
        }
    }
    [ClientRpc]
    private void TriggerShootAnimClientRpc()
    {
        // Mindenki lejátssza a lövés animációt
        if (animator != null && isHunter.Value) animator.SetTrigger(animIDShoot);
    }
    [ClientRpc]
    private void SetReloadAnimClientRpc(bool reloading)
    {
        if (animator != null && isHunter.Value) animator.SetBool(animIDReload, reloading);
    }
}