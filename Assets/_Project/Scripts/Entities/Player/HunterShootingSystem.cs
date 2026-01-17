using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerNetworkController))]
public class HunterShootingSystem : NetworkBehaviour
{
    [Header("Fegyver Statisztikák")]
    [SerializeField] private int damage = 25;
    [SerializeField] private float fireRate = 0.5f;
    [SerializeField] private float range = 100f;
    [SerializeField] private LayerMask shootableLayers;

    [Header("Oh Deer Mechanika")]
    [SerializeField] private float playerHitReward = 15f;
    [SerializeField] private float npcHitPenalty = 25f;

    [Header("Visuals")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private Transform firePoint;

    private PlayerNetworkController playerController;
    private float nextFireTime = 0f;
    private Camera cam;
    private bool canShoot = false;

    public override void OnNetworkSpawn()
    {
        playerController = GetComponent<PlayerNetworkController>();
    }
    public void ResetShootingState()
    {
        canShoot = false;
        this.enabled = true;
    }
    public void EnableShooting(bool enable)
    {
        canShoot = enable;
    }
    private void Update()
    {
        if (!IsOwner) return;
        if (!playerController.isHunter.Value || !canShoot) return;
        if (Mouse.current.leftButton.isPressed && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    private void Shoot()
    {
        if (muzzleFlash != null) muzzleFlash.Play();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, range, shootableLayers))
        {
            NetworkObject targetNetObj = hit.collider.GetComponentInParent<NetworkObject>();

            if (targetNetObj != null)
            {
                Debug.Log($"Célpont azonosítva: {targetNetObj.name} (ID: {targetNetObj.NetworkObjectId})");
                ShootServerRpc(targetNetObj.NetworkObjectId);
            }
            else
            {
                Debug.LogWarning("Találat van, de nincs rajta (vagy a szülõjén) NetworkObject!");
            }
        }
    }

    [ServerRpc]
    private void ShootServerRpc(ulong targetId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject targetObj))
        {
            var myHealth = GetComponent<HealthComponent>();
            if (targetObj.GetComponent<PlayerNetworkController>() != null)
            {
                if (targetObj.TryGetComponent(out HealthComponent targetHealth))
                {
                    targetHealth.TakeHit(9999);
                }
                myHealth.ModifyHealth(playerHitReward);
            }
            else if (targetObj.GetComponent<Npc>() != null)
            {
                targetObj.Despawn(true);
                myHealth.ModifyHealth(-npcHitPenalty);
            }
        }
    }
}