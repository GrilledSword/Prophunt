using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerNetworkController))]
public class HunterShootingSystem : NetworkBehaviour
{
    [Header("Fegyver Statisztikák")]
    [SerializeField] private float range = 100f;
    [SerializeField] private LayerMask shootableLayers;

    [Header("Oh Deer Mechanika")]
    [SerializeField] private float playerHitReward = 15f;
    [SerializeField] private float npcHitPenalty = 25f;

    [Header("Visuals")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private Transform firePoint; // Ha van fegyvercsõ vége pontod

    private PlayerNetworkController playerController;
    private Camera cam;

    public override void OnNetworkSpawn()
    {
        playerController = GetComponent<PlayerNetworkController>();
    }

    // [MODIFIED] Kivettem az Update metódust! 
    // Mostantól nem figyeli az egeret, csak várja a parancsot.

    public void ResetShootingState()
    {
        // Ide jöhet bármi reset logika, ha kell
    }

    // [MODIFIED] Ezt hívja meg a PlayerNetworkController
    // Visszatérhetne bool-lal, ha számítana a fireRate, de most a Reload animáció korlátoz.
    public void TryShoot()
    {
        if (!IsOwner) return;

        // Visuals
        if (muzzleFlash != null) muzzleFlash.Play();

        // Raycast logika
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // A képernyõ közepére lövünk (ahova a célkereszt mutat)
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, range, shootableLayers))
        {
            // Debug, hogy lássuk mit találtunk el
            // Debug.DrawLine(ray.origin, hit.point, Color.red, 2f);

            NetworkObject targetNetObj = hit.collider.GetComponentInParent<NetworkObject>();

            if (targetNetObj != null)
            {
                ShootServerRpc(targetNetObj.NetworkObjectId);
            }
        }
    }

    [ServerRpc]
    private void ShootServerRpc(ulong targetId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject targetObj))
        {
            var myHealth = GetComponent<HealthComponent>();

            // 1. Eltaláltunk egy Játékost?
            if (targetObj.GetComponent<PlayerNetworkController>() != null)
            {
                if (targetObj.TryGetComponent(out HealthComponent targetHealth))
                {
                    // A Prop Hunt szabályai szerint a találat insta-kill vagy nagy sebzés
                    targetHealth.TakeHit(9999);
                }

                // Jutalmazzuk a vadászt
                if (myHealth != null)
                {
                    myHealth.ModifyHealth(playerHitReward);
                }
            }
            // 2. Eltaláltunk egy NPC-t?
            else if (targetObj.GetComponent<DeerAIController>() != null)
            {
                // Büntetés: levonjuk az életet
                if (myHealth != null)
                {
                    myHealth.ModifyHealth(-npcHitPenalty);
                }

                // Opcionális: Az NPC meghal/eltûnik?
                targetObj.Despawn(true); 
            }
        }
    }
}