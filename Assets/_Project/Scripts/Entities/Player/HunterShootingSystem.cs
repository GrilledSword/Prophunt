using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerNetworkController))]
public class HunterShootingSystem : NetworkBehaviour
{
    [Header("Fegyver Statisztikák")]
    [SerializeField] private int damage = 25;
    [SerializeField] private float fireRate = 0.5f; // Lövés másodpercenként
    [SerializeField] private float range = 100f;
    [SerializeField] private LayerMask shootableLayers; // Mit lehet eltalálni?

    [Header("Oh Deer Mechanika")]
    [SerializeField] private float playerHitReward = 15f; // Mennyi Sanity-t kap vissza, ha játékost lõ?
    [SerializeField] private float npcHitPenalty = 25f;   // Mennyi Sanity-t veszít, ha NPC-t lõ?

    [Header("Visuals")]
    [SerializeField] private ParticleSystem muzzleFlash; // Torkolattûz (Opcionális)
    [SerializeField] private Transform firePoint; // Honnan indul a golyó (FPS Mountnál)

    private PlayerNetworkController playerController;
    private float nextFireTime = 0f;
    private Camera cam;

    public override void OnNetworkSpawn()
    {
        playerController = GetComponent<PlayerNetworkController>();
    }

    private void Update()
    {
        // Csak a saját karakterünk lõhet, és CSAK ha Hunter!
        if (!IsOwner) return;
        if (!playerController.isHunter.Value) return;

        // Fegyver kezelés
        if (Mouse.current.leftButton.isPressed && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    private void Shoot()
    {
        // 1. Kliens oldali vizuál
        if (muzzleFlash != null) muzzleFlash.Play();

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // 2. Raycast
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, range, shootableLayers))
        {
            Debug.Log($"Találat: {hit.collider.name}"); // Ez írta ki, hogy DeerMesh

            // [JAVÍTÁS START] ---------------------------------------------------------
            // Nem elég a hit.collider-en keresni, mert lehet, hogy egy gyerek objektumot (Mesh) találtunk el.
            // A GetComponentInParent felfelé halad a hierarchiában, amíg nem talál egy NetworkObject-et.

            NetworkObject targetNetObj = hit.collider.GetComponentInParent<NetworkObject>();

            if (targetNetObj != null)
            {
                Debug.Log($"Célpont azonosítva: {targetNetObj.name} (ID: {targetNetObj.NetworkObjectId})");

                // Küldjük a szervernek, hogy kire lõttünk
                ShootServerRpc(targetNetObj.NetworkObjectId);
            }
            else
            {
                Debug.LogWarning("Találat van, de nincs rajta (vagy a szülõjén) NetworkObject!");
            }
            // [JAVÍTÁS END] -----------------------------------------------------------
        }
    }

    [ServerRpc]
    private void ShootServerRpc(ulong targetId)
    {
        // 1. Megkeressük, mit találtunk el
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject targetObj))
        {
            // 2. Megnézzük a SAJÁT életünket (hogy tudjunk büntetni/jutalmazni)
            var myHealth = GetComponent<HealthComponent>();

            // 3. ESET: JÁTÉKOST TALÁLTUNK (Szarvas Játékos)
            if (targetObj.GetComponent<PlayerNetworkController>() != null)
            {
                // A célpont (Szarvas) meghal (TakeHit kezeli az instant halált)
                if (targetObj.TryGetComponent(out HealthComponent targetHealth))
                {
                    targetHealth.TakeHit(9999); // Biztos halál
                }

                // Mi (Vadász) kapunk Sanity-t
                myHealth.ModifyHealth(playerHitReward);
                Debug.Log("Játékos találat! Sanity nõtt.");
            }
            // 4. ESET: NPC-T TALÁLTUNK
            else if (targetObj.GetComponent<Npc>() != null)
            {
                // Az NPC "meghal" (eltûnik)
                targetObj.Despawn(true);

                // Mi (Vadász) sérülünk (Büntetés)
                myHealth.ModifyHealth(-npcHitPenalty);
                Debug.Log("NPC találat! Sanity csökkent (Büntetés).");
            }
        }
    }
}