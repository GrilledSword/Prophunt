using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Ez a script felel azért, hogy a szarvas át tudjon változni
[RequireComponent(typeof(PlayerNetworkController))]
public class DeerPropSystem : NetworkBehaviour
{
    [Header("Beállítások")]
    [SerializeField] private float interactRange = 3.5f; // Milyen messzirõl tud átváltozni
    [SerializeField] private LayerMask propLayer; // Csak "Prop" layereken lévõ tárgyakra mûködjön

    [Header("References")]
    // [MODULARITÁS] Külön kezeljük a vizuális referenciákat
    [SerializeField] private MeshFilter deerMeshFilter;     // A játékos (Szarvas) MeshFiltere
    [SerializeField] private MeshRenderer deerMeshRenderer; // A játékos (Szarvas) Renderere

    // Elmentjük az eredeti formánkat, hogy vissza tudjunk változni
    private Mesh originalMesh;
    private Material originalMaterial;

    private PlayerNetworkController playerController;

    public override void OnNetworkSpawn()
    {
        playerController = GetComponent<PlayerNetworkController>();

        // [INIT] Elmentjük az eredeti ("szarvas") kinézetet induláskor
        if (deerMeshFilter != null) originalMesh = deerMeshFilter.sharedMesh;
        if (deerMeshRenderer != null) originalMaterial = deerMeshRenderer.sharedMaterial;
    }

    private void Update()
    {
        // [CHECK] Csak a saját karakterünkön futtatjuk, és CSAK ha mi vagyunk a tulajdonosok
        if (!IsOwner) return;

        // [LOGIC] Ha Vadászok vagyunk, akkor ez a script ne csináljon semmit (ne változzon át a vadász dobozzá)
        if (playerController.isHunter.Value) return;

        HandleInput();
    }

    private void HandleInput()
    {
        // Bal egérgomb (vagy érintés tap)
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryTransform();
        }

        // 'R' gomb reseteléshez (opcionális debug funkció)
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            RequestResetAppearanceServerRpc();
        }
    }

    private void TryTransform()
    {
        // [RAYCAST] Lövünk egy sugarat a kamera közepébõl
        if (Camera.main == null) return;

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        // Megnézzük, eltaláltunk-e valamit a megadott távolságon belül
        if (Physics.Raycast(ray, out hit, interactRange, propLayer))
        {
            // Megnézzük, hogy a találaton van-e NetworkObject komponens
            // (Ez kritikus, mert ID alapján szinkronizálunk!)
            if (hit.collider.TryGetComponent(out NetworkObject targetNetObj))
            {
                // [NETWORK] Küldjük a kérést a szervernek: "Erre az ID-jú tárgyra akarok hasonlítani"
                RequestPropChangeServerRpc(targetNetObj.NetworkObjectId);
            }
            else
            {
                Debug.LogWarning("Erre a tárgyra nem lehet átváltozni, mert nincs rajta NetworkObject!");
            }
        }
    }

    // ---------------- RPC HÍVÁSOK (Hálózati kommunikáció) ----------------

    // 1. A Kliens kéri a Szervert
    [ServerRpc]
    private void RequestPropChangeServerRpc(ulong targetObjectId)
    {
        // (Itt lehetne validálni, hogy a játékos tényleg közel van-e a tárgyhoz - anti-cheat)

        // 2. A Szerver utasít MINDEN Klienst (beleértve a küldõt is)
        ChangePropClientRpc(targetObjectId);
    }

    [ServerRpc]
    private void RequestResetAppearanceServerRpc()
    {
        ResetPropClientRpc();
    }

    // 3. Ez fut le minden játékos gépén
    [ClientRpc]
    private void ChangePropClientRpc(ulong targetObjectId)
    {
        // Megkeressük a hálózaton az objektumot az ID alapján
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetObjectId, out NetworkObject targetObj))
        {
            // Leszedjük a célpont kinézetét
            MeshFilter targetMF = targetObj.GetComponent<MeshFilter>();
            MeshRenderer targetMR = targetObj.GetComponent<MeshRenderer>();

            if (targetMF == null || targetMR == null) return;

            // [VISUAL UPDATE] Átmásoljuk a saját komponenseinkre
            if (deerMeshFilter) deerMeshFilter.mesh = targetMF.sharedMesh;
            if (deerMeshRenderer) deerMeshRenderer.material = targetMR.sharedMaterial;

            // Debug üzenet, hogy lássuk mûködik-e
            // Debug.Log($"Átváltozás sikeres: {targetObj.name}");
        }
    }

    [ClientRpc]
    private void ResetPropClientRpc()
    {
        if (deerMeshFilter) deerMeshFilter.mesh = originalMesh;
        if (deerMeshRenderer) deerMeshRenderer.material = originalMaterial;
    }
}