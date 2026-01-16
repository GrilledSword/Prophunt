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
    [SerializeField] private float rotationSmoothTime = 0.1f; // Milyen gyorsan forduljon a szarvas
    [SerializeField] private float gravity = -9.81f;

    [Header("Dash (Csak Szarvas)")]
    [SerializeField] private float dashForce = 20f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 3f;

    [Header("Kamera Beállítások")]
    [SerializeField] private float mouseSensitivity = 2.0f; // Egér sebessége
    [SerializeField] private float tpsCameraDistance = 4.0f; // Milyen messze legyen a kamera a szarvastól
    [SerializeField] private Vector2 pitchLimits = new Vector2(-70f, 80f); // Fel-le nézés limit

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

    // Kamera forgás változók
    private float cameraPitch = 0f; // Fel-Le
    private float cameraYaw = 0f;   // Jobbra-Balra
    private float currentRotationVelocity; // Simításhoz

    // Dash változók
    private bool isDashing;
    private float dashEndTime;
    private float lastDashTime;

    private bool isGhost = false;
    private bool isUIConnected = false;

    private bool isTrapped = false;
    private int trapEscapePressesNeeded = 10; // Hányszor kell megnyomni a Space-t
    private int currentTrapPresses = 0;

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

            // Kezdõ forgás beállítása
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
            Debug.Log("[Player] HUD megtalálva! Csatlakozás...");
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
        // [JAVÍTÁS] Ha változik a szerep, a HP komponensnek is szólni kell!
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

        // 1. SZELLEM MÓD (Spectator)
        if (isGhost)
        {
            HandleInput(); // Fontos: Inputot olvasni kell!
            MoveGhost();
            return;
        }

        // 2. MEDVECSAPDA
        if (isTrapped) { HandleTrapEscape(); return; }

        // 3. HUNTER RELEASE (Bújócska fázis)
        // Ha Vadász vagyunk ÉS még tart a Release fázis -> NEM MOZOGHATUNK!
        if (isHunter.Value && NetworkGameManager.Instance != null && NetworkGameManager.Instance.IsHunterRelease())
        {
            // UI üzenet mehetne ide: "WAIT FOR DEER TO HIDE..."
            // Opcionális: Feketére állítani a képernyõt (Vakság)
            return;
        }

        // 4. NORMÁL JÁTÉK
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
            // FPS: Pontosan a mounton (szemben)
            sceneCamera.transform.position = targetMount.position;
            sceneCamera.transform.rotation = targetMount.rotation;
        }
        else
        {
            // TPS: A mount mögött "tpsCameraDistance" távolságra
            // Mivel a LookDeer() már beforgatta a tpsMount-ot a megfelelõ irányba,
            // csak hátra kell lépnünk a mount forward irányából.
            Vector3 targetPos = targetMount.position - (targetMount.forward * tpsCameraDistance);

            // (Opcionális: Itt lehetne fal-detektálást betenni Raycasttal, hogy ne menjen át a falon)

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
        if (!characterController.enabled) return; // [ÚJ] Védelem
        ApplyGravity();

        float speed = GetCurrentSpeed();
        // A mozgás a test irányához (transform) képest történik
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        characterController.Move(move * speed * Time.deltaTime);
    }
    private void MoveDeer()
    {
        if (!characterController.enabled) return; // [ÚJ] Védelem
        ApplyGravity();

        // DASH KEZELÉS
        if (isDashing)
        {
            if (Time.time < dashEndTime)
            {
                // Dash irány: amerre a karakter épp néz
                Vector3 dashDir = transform.forward;
                if (moveInput.magnitude > 0 && sceneCamera != null)
                {
                    // Vagy amerre nyomjuk, a kamerához képest
                    float targetAngle = Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg + sceneCamera.transform.eulerAngles.y;
                    dashDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
                }
                characterController.Move(dashDir * dashForce * Time.deltaTime);
                return;
            }
            else isDashing = false;
        }

        // MOZGÁS KAMERA RELATÍV
        if (moveInput.magnitude >= 0.1f && sceneCamera != null)
        {
            // Kiszámoljuk, merre van az "elõre" a kamerához képest
            // Mathf.Atan2 a bemeneti vektor szögét adja meg
            // Hozzáadjuk a kamera Y forgását
            float targetAngle = Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg + sceneCamera.transform.eulerAngles.y;

            // Simítjuk a forgást, hogy ne pattanjon a karakter
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref currentRotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            // A kiszámolt irányba mozgunk
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

        // A testet forgatjuk horizontálisan
        transform.Rotate(Vector3.up * mouseX);

        // A Mountot (és a kamerát) bólintjuk vertikálisan
        if (fpsMount != null)
            fpsMount.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }
    private void LookDeer()
    {
        float mouseX = lookInput.x * mouseSensitivity * Time.deltaTime * 10f;
        float mouseY = lookInput.y * mouseSensitivity * Time.deltaTime * 10f;

        // Csak a belsõ változókat frissítjük, a testet NEM forgatjuk
        cameraYaw += mouseX;
        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, pitchLimits.x, pitchLimits.y);

        // A TPS Mountot forgatjuk a világban (ez a pivot pont a fejünknél)
        if (tpsMount != null)
        {
            tpsMount.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        }
    }
    private void MoveGhost()
    {
        // [JAVÍTÁS] Driftelés ellen: Ha nincs input, nincs mozgás.
        if (moveInput.magnitude < 0.1f && !Keyboard.current.spaceKey.isPressed && !Keyboard.current.leftCtrlKey.isPressed)
        {
            return;
        }

        LookHunter(); // Forgás ugyanaz, mint FPS

        float speed = 15f;
        // Fontos: Transform.Translate-t használunk, ami lokális irányba mozgat
        Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y);

        // Fel-le
        if (Keyboard.current.spaceKey.isPressed) moveDir.y = 1;
        if (Keyboard.current.leftCtrlKey.isPressed) moveDir.y = -1;

        transform.Translate(moveDir * speed * Time.deltaTime);
    }
    [ServerRpc]
    private void ApplySprintCostServerRpc()
    {
        if (isHunter.Value && healthComponent.isDraining.Value)
        {
            healthComponent.ModifyHealth(-hunterSprintCost * Time.deltaTime);
        }
    }
    [ClientRpc]
    public void SetGhostModeClientRpc()
    {
        if (!IsOwner) return;

        Debug.Log("Spectator Mód Aktiválva!");
        isGhost = true; // [ÚJ] Jelezzük, hogy szellemek vagyunk

        // 1. Fizika kikapcsolása
        if (characterController != null) characterController.enabled = false;

        // 2. Modellek eltüntetése
        if (hunterModel) hunterModel.SetActive(false);
        if (deerModel) deerModel.SetActive(false);

        // 3. UI frissítés (opcionális)
        // GameHUD.Instance.ShowSpectatorUI();
    }
    // [NEW] Hunter Pánik Mód (Amikor elfogy a Sanity)
    [ClientRpc]
    public void TransformToPanicModeClientRpc()
    {
        Debug.Log("A VADÁSZBÓL PRÉDA LETT! FUSS!");

        // 1. Fegyver elvétele (Kikapcsoljuk a ShootingSystemet)
        var shootingSystem = GetComponent<HunterShootingSystem>();
        if (shootingSystem != null) shootingSystem.enabled = false;

        // 2. Vizuális csere: Legyen Szarvas (vagy ami be van állítva deerModel-nek)
        // Mivel a `UpdateVisuals` a `isHunter` alapján dönt, át kell vernünk a rendszert,
        // vagy bevezetni egy új változót. A legegyszerûbb:

        if (IsOwner)
        {
            // Hunterként eddig nem láttuk magunkat (FPS), most váltsunk TPS-re!
            // Ehhez át kell állítani a kamerát a TPS Mountra.
            // A legegyszerûbb, ha "szoftveresen" átírjuk, hogy mostantól "Szarvas" a logikánk.
            // DE! A szerver tudja, hogy Hunterek vagyunk.

            // MVP MEGOLDÁS: Csak a vizuált és a kamerát állítjuk át lokálisan.
            hunterModel.SetActive(false);
            deerModel.SetActive(true); // Látjuk magunkat

            // Kamera átállítása TPS-re (A UpdateCameraPosition-t kell "becsapni")
            // Ezt egy bool flaggel oldjuk meg.
        }
        else
        {
            // Mások is lássák, hogy átváltozott!
            hunterModel.SetActive(false);
            deerModel.SetActive(true);
        }

        // Sprint korlát levétele (Adrenalin!)
        hunterSprintCost = 0f;

        // UI üzenet: "RUN TO THE SAFE ZONE!" (GameHUD hívás)
    }
    private void HandleTrapEscape()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            currentTrapPresses++;
            // UI visszajelzés (pl. egy csúszka) jöhet ide
            Debug.Log($"Szabadulás: {currentTrapPresses}/{trapEscapePressesNeeded}");

            if (currentTrapPresses >= trapEscapePressesNeeded)
            {
                // Sikerült kiszabadulni!
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
            Debug.Log("MEDVECSAPDA! NYOMKODD A SPACE-T!");
            // Itt játszhatsz le csattanó hangot
        }
        else
        {
            Debug.Log("KISZABADULTÁL!");
        }
    }
    // [NEW] Kliens kéri: "Kiszabadultam!"
    [ServerRpc]
    private void RequestUntrapServerRpc()
    {
        // Kiszabadítjuk mindenkinél
        SetTrappedClientRpc(false);
    }
    [ClientRpc]
    public void ResetPlayerStateClientRpc()
    {
        // 1. Változók reset
        isGhost = false;
        isTrapped = false;
        isDashing = false;

        var shooting = GetComponent<HunterShootingSystem>();
        if (shooting != null) shooting.ResetShootingState();

        // 2. Fizika visszakapcsolása
        if (characterController != null) characterController.enabled = true;

        // 3. Gravitáció visszaállítása (ha a Ghost mód elállította)
        gravity = -9.81f;
        walkSpeed = 5f; // Eredeti érték
        sprintSpeed = 9f; // Eredeti érték

        // 4. Modellek frissítése (Mindenki szarvas a Lobbyban, vagy saját magát látja)
        // A 'ResetAllPlayersToLobbyState' már beállította az isHunter-t false-ra, 
        // így az OnValueChanged le fog futni és frissíti a visualt.
        // De biztosra mehetünk:
        UpdateVisuals(false);

        // 5. HUD Reset (Célkereszt eltüntetése)
        if (IsOwner && GameHUD.Instance != null)
        {
            GameHUD.Instance.SetRoleUI(false); // False = Szarvas nézet (nincs célkereszt)
        }

        Debug.Log("[Player] Reset kész. Lobby állapot.");
    }
}