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
    [Header("Mozg�s Be�ll�t�sok")]
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
    [SerializeField] private float dashJumpForce = 15f; // Ugrás komponens

    [Header("Kamera Be�ll�t�sok")]
    [SerializeField] private float mouseSensitivity = 0.5f;
    [SerializeField] private float tpsCameraDistance = 4.0f;
    [SerializeField] private Vector2 pitchLimits = new Vector2(-70f, 80f);

    [Header("Kamera Stabiliz�tor (FPS)")]
    [SerializeField] private bool useStabilizer = true; // Kapcsol�
    [SerializeField] private float posLerpSpeed = 15f;

    [Header("Harc Be�ll�t�sok (Hunter)")]
    [SerializeField] private float reloadTime = 0.1f; // Mennyi ideig tart az �j ny�l el�v�tele
    [SerializeField] private float shootAnimDuration = 0.1f; // Mennyi id� a l�v�s anim�ci�

    [Header("Audio & Anim�ci�")]
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
    private NetworkVariable<bool> isDeerEvilMode = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // [NEW] Evil/Panic mód

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

    // [ADDED] �j �llapotjelz�k a mozg�s korl�toz�s�hoz
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
    private int animIDDash;   // [ADDED] Dash anim
    private int animIDDeerAttack; // [ADDED] Szarvas támadás anim
    private int animIDIsEvilDeer; // [NEW] Evil mód BlendTree váltáshoz


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
        animIDDash = Animator.StringToHash("Dash");
        animIDDeerAttack = Animator.StringToHash("DeerAttack");
        animIDIsEvilDeer = Animator.StringToHash("IsEvilDeer");

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

        // [�J] Figyelj�k a v�ltoz�st, hogy azonnal friss�lj�n az anim�ci�
        isAimingNetworked.OnValueChanged += OnAimingStateChanged;
        isDeerEvilMode.OnValueChanged += OnDeerEvilModeChanged; // [NEW] Evil mód figyelése

        UpdateVisuals(isHunter.Value);
    }
    private void OnDeerEvilModeChanged(bool previous, bool current)
    {
        // Szarvasok BlendTree váltása
        if (animator != null && !isHunter.Value)
        {
            animator.SetBool(animIDIsEvilDeer, current);
            Debug.Log($"[PlayerNetworkController] Szarvas mód: {(current ? "Evil/Panic" : "Normal")}");
        }
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
        if (isMimicEating != null)
            isMimicEating.OnValueChanged -= OnMimicEatingChanged;
        if (isAimingNetworked != null)
            isAimingNetworked.OnValueChanged -= OnAimingStateChanged;
        if (isDeerEvilMode != null)
            isDeerEvilMode.OnValueChanged -= OnDeerEvilModeChanged; // [NEW]
        if (isHunter != null)
            isHunter.OnValueChanged -= OnRoleChanged;
        
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
        // Single conditional structure instead of duplicate if-else
        if (hunterModel != null) hunterModel.SetActive(hunterParams);
        if (deerModel != null) deerModel.SetActive(!hunterParams);

        // Get animator from the active model
        GameObject targetModel = hunterParams ? hunterModel : deerModel;
        if (targetModel != null)
        {
            animator = targetModel.GetComponentInChildren<Animator>(true);
        }
    }
    private void Update()
    {
        // [JAV�T�S] Sz�tv�lasztottuk az Update logik�t.
        // A HandleAnimations MINDENKINEK lefut, nem csak az Ownernek!

        if (IsOwner)
        {
            HandleOwnerLogic();
        }

        // [JAV�T�S] Anim�ci�t mindenki friss�t (Owner �s Proxy is)
        CalculateNetworkVelocity();
        HandleAnimations();
    }
    private void HandleOwnerLogic()
    {
        if (!isUIConnected) TryConnectToHUD();

        if (isGhost) { HandleInput(); MoveGhost(); return; }
        if (isTrapped) { HandleTrapEscape(); return; }

        HandleInteractionInput();
        HandleInput(); // Mozg�s input (WASD)

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
            // Ownern�l a CharacterController pontos �rt�ket ad
            calculatedSpeed = new Vector3(characterController.velocity.x, 0, characterController.velocity.z).magnitude;
        }
        else
        {
            // Proxykn�l (akiket a h�l�zat mozgat) a poz�ci� v�ltoz�s�b�l sz�molunk
            Vector3 currentPos = transform.position;
            float dist = Vector3.Distance(new Vector3(currentPos.x, 0, currentPos.z), new Vector3(lastPosition.x, 0, lastPosition.z));

            // Ha a delta id� nagyon kicsi, 0-val osztan�nk, ezt ker�lj�k el
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

        // 1. �ltal�nos GameState ellen�rz�s
        if (NetworkGameManager.Instance != null && NetworkGameManager.Instance.currentGameState.Value != GameState.InGame)
            canCombat = false;

        // 2. [JAV�T�S] Specifikus P�nik ellen�rz�s
        // Ha P�nik m�d van, akkor NINCS harc, pont.
        if (isPanicMode) canCombat = false;

        if (!canCombat)
        {
            // Fail-safe: Ha valahogy m�gis TRUE maradt a h�l�zati v�ltoz� (pl. lag miatt),
            // �s mi vagyunk a tulajok, akkor k�nyszer�tj�k a FALSE-t.
            if (IsOwner && isAimingNetworked.Value)
            {
                SetAimingServerRpc(false);
            }
            return; // Itt azonnal kil�p�nk, �gy az Input le se fut!
        }

        // --- INNEN CSAK AKKOR FUT LE, HA MINDEN OK� (Nincs P�nik) ---

        if (Mouse.current != null && !isReloading)
        {
            bool rightClickHeld = Mouse.current.rightButton.isPressed;

            // Ha v�ltozott az input �llapota, jelezz�k a szervernek
            if (isAimingNetworked.Value != rightClickHeld)
            {
                SetAimingServerRpc(rightClickHeld);
            }
        }
        else if (isReloading && isAimingNetworked.Value)
        {
            // Ha t�lt�nk, nem c�lozhatunk
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

        // A Shoot Trigger egy "esem�ny", azt RPC-vel k�ldj�k �t, hogy mindenki lej�tssza
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
                // [JAV�T�S] Most m�r a h�l�zati v�ltoz�t n�zz�k!
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
        isReloading = reloading; // Ez blokkolja majd a mozg�st a GetCurrentSpeed-ben

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
        if (isPanicMode) return; // Pánik módban nem lehet enni

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
        
        // Szarvas ütközés detektálása Panic módban
        if (!isHunter.Value && isPanicMode && characterController.isGrounded)
        {
            CheckDeerPanicCollisions();
        }
    }
    private void CheckDeerPanicCollisions()
    {
        // CSAK pánik módban lehet támadni
        if (!isPanicMode) return;
        
        // Kis sugárral körül nézünk, hogy találunk-e vadászt
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, 2f);
        
        foreach (var collider in hitColliders)
        {
            if (collider.CompareTag("Player"))
            {
                var otherPlayer = collider.GetComponent<PlayerNetworkController>();
                if (otherPlayer != null && otherPlayer.isHunter.Value)
                {
                    // Megtámadunk egy vadászt (CSAK pánik módban!)
                    AttackHunterServerRpc(otherPlayer.NetworkObjectId);
                }
            }
        }
    }
    [ServerRpc]
    private void AttackHunterServerRpc(ulong hunterNetId)
    {
        // CSAK pánik módban lehet támadni!
        if (!NetworkGameManager.Instance.IsHunterPanic()) return;
        if (isHunter.Value) return; // Szarvasok támadnak, nem hunterek
        
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(hunterNetId, out NetworkObject hunterObj))
        {
            var hunterHealth = hunterObj.GetComponent<HealthComponent>();
            if (hunterHealth != null && hunterHealth.isHunter)
            {
                hunterHealth.ModifyHealth(-25f); // 25 sebzés
                Debug.Log($"[PlayerNetworkController] Szarvas megtámadott egy vadászt!");
                
                // Szinkronizáljuk a támadás animációt minden kliens számára
                TriggerDeerAttackAnimClientRpc();
            }
        }
    }
    private void UpdateCameraPosition()
    {
        bool useFps = isHunter.Value && !isPanicMode;
        Transform targetMount = useFps ? fpsMount : tpsMount;

        if (targetMount == null) return;

        if (useFps)
        {
            // FPS M�D (Itt kell a stabiliz�tor)
            if (useStabilizer)
            {
                // 1. Poz�ci�: Lerp-elj�k, hogy a kis r�zk�d�sokat kisim�tsuk
                sceneCamera.transform.position = Vector3.Lerp(
                    sceneCamera.transform.position,
                    targetMount.position,
                    Time.deltaTime * posLerpSpeed
                );

                // 2. Rot�ci�: EZ A TITOK!
                // NEM vessz�k �t a targetMount.rotation-t (mert az a fejcsonttal egy�tt r�zk�dik).
                // Helyette a tiszta Input alap� rot�ci�t haszn�ljuk (CameraPitch + Test Yaw).
                // �gy a fej mozoghat alattunk, de a kamera stabil marad, mint egy igazi FPS-ben.
                Quaternion stableRotation = Quaternion.Euler(cameraPitch, transform.eulerAngles.y, 0f);
                sceneCamera.transform.rotation = stableRotation;
            }
            else
            {
                // R�gi, "kem�ny" k�t�s (r�zk�d�s)
                sceneCamera.transform.position = targetMount.position;
                sceneCamera.transform.rotation = targetMount.rotation;
            }
        }
        else
        {
            // TPS M�D (Szarvas / P�nik) - Itt maradhat a r�gi logika
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

            if (!isHunter.Value && !isPanicMode && Keyboard.current.leftAltKey.wasPressedThisFrame)
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
            
            // Szinkronizáljuk a Dash animációt
            TriggerDashAnimServerRpc();
        }
    }
    private void MoveHunter()
    {
        if (!characterController.enabled) return;
        ApplyGravity();

        float speed = GetCurrentSpeed();
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        // Ha a sebess�g 0 (pl. t�lt�s miatt), a move vektor is null�z�dik
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
                // Horizontális mozgás
                characterController.Move(dashDir * dashForce * Time.deltaTime);
                
                // Vertikális komponens (ugrás/vetődés)
                velocity.y = dashJumpForce;
                characterController.Move(velocity * Time.deltaTime);
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
        // 1. P�nik m�d fel�lb�r�l mindent -> MAX SEBESS�G
        // Fontos: Itt m�r nem �rdekel minket, hogy c�loz-e a j�t�kos, mert a P�nik fel�l�rja.
        if (isPanicMode) return hunterSprintSpeed;

        if (isHunter.Value && isReloading) return 0f;

        bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        if (isHunter.Value)
        {
            // Ha nincs p�nik, �s c�lzunk, akkor lass� s�ta
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

        // Ez forgatja a fegyvert/kart a modellen (vizu�lis), de a kamera m�r a "Matek"-ot k�veti fentr�l
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
            Debug.Log($"Szabadul�s: {currentTrapPresses}/{trapEscapePressesNeeded}");

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
        // Haszn�lhatn�nk NetworkVariable-t is, de az anim�tor param�ter szinkronhoz most j� a ClientRpc is,
        // vagy friss�thetn�nk egy NetworkVariable-t. Maradjunk az RPC-n�l az egyszer�s�g kedv��rt.
        SetReloadAnimClientRpc(reloading);
    }
    [ServerRpc]
    private void RequestUntrapServerRpc()
    {
        SetTrappedClientRpc(false);
    }
    [ServerRpc]
    private void TriggerDashAnimServerRpc()
    {
        // Szinkronizáljuk a Dash animációt minden kliensnek
        TriggerDashAnimClientRpc();
    }
    [ServerRpc]
    private void SetDeerEvilModeServerRpc(bool isEvil)
    {
        // [NEW] Szinkronizáljuk a BlendTree módot
        isDeerEvilMode.Value = isEvil;
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

        Debug.Log("Spectator M�d Aktiv�lva!");
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

        // [JAV�T�S] K�nyszer�tett reset (Kill Switch)
        // Azonnal le�ll�tjuk a helyi �llapotokat
        isReloading = false;

        // Anim�tor takar�t�s azonnal (hogy ne maradjon vizu�lisan beragadva)
        if (animator != null)
        {
            animator.SetBool(animIDAiming, false);
            animator.SetBool(animIDReload, false);
            // Opcion�lis: Ha van "Equip" vagy hasonl� layer, azt is resetelni kellene
        }

        // Ha mi vagyunk a tulajdonosok, k�z�ln�nk kell a h�l�zattal is, hogy "befejeztem a c�lz�st"
        // Ez oldja meg a beragad�st, ha nyomva tartod a gombot
        if (IsOwner)
        {
            // Csak akkor k�ld�nk RPC-t, ha a h�l�zati v�ltoz� szerint m�g c�lzunk
            if (isAimingNetworked.Value)
            {
                SetAimingServerRpc(false);
            }
        }

        UpdateVisuals(isHunter.Value);
        
        // ===== [NEW] SZARVASOK PANIC MODE-BAN =====
        if (!isHunter.Value)
        {
            // Szarvasok "szarvakkal felszerelt" modelé válnak - ezt az UpdateVisuals meg kellene valósítania
            // Szinkronizáljuk, hogy a szarvas sebezhet
            if (healthComponent != null)
            {
                healthComponent.SetPanicModeActiveRpc(true);
            }
            
            // [NEW] BlendTree váltás: NormalDeer → EvilDeer
            SetDeerEvilModeServerRpc(true);
            
            Debug.Log("[Player] Szarvas átváltozik Panic Module-ban - Sebzés képessége aktiválva!");
        }

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
        
        // [FIX] Animator csak akkor, ha létezik
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
            
            // Unity ignorálja a nem létező paramétereket
            animator.SetBool(animIDIsEating, false);
            animator.SetBool(animIDAiming, false);
            animator.SetBool(animIDReload, false);
            animator.SetFloat(animIDSpeed, 0f);
            animator.SetBool(animIDIsEvilDeer, false);
        }
        
        if (IsServer)
        {
            isMimicEating.Value = false;
            isAimingNetworked.Value = false;
            isDeerEvilMode.Value = false; // [NEW] Vissza Normal módra
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
        // Mindenki lej�tssza a l�v�s anim�ci�t
        if (animator != null && isHunter.Value) animator.SetTrigger(animIDShoot);
    }
    [ClientRpc]
    private void TriggerDashAnimClientRpc()
    {
        // Mindenki lej�tssza a Dash anim�ci�t (csak szarvasoknak)
        if (animator != null && !isHunter.Value) animator.SetTrigger(animIDDash);
    }
    [ClientRpc]
    private void TriggerDeerAttackAnimClientRpc()
    {
        // Mindenki lej�tssza a szarvas t�mad�s anim�ci�t
        if (animator != null && !isHunter.Value) animator.SetTrigger(animIDDeerAttack);
    }
    [ClientRpc]
    private void SetReloadAnimClientRpc(bool reloading)
    {
        if (animator != null && isHunter.Value) animator.SetBool(animIDReload, reloading);
    }
}