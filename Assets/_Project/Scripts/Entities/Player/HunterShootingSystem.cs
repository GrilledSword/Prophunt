using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerNetworkController))]
public class HunterShootingSystem : NetworkBehaviour
{
    [Header("Íj Beállítások")]
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float shootForce = 40f;

    [Header("Visuals")]
    [SerializeField] private ParticleSystem muzzleFlash;

    private PlayerNetworkController playerController;
    private bool isShootingEnabled = true;

    public override void OnNetworkSpawn()
    {
        playerController = GetComponent<PlayerNetworkController>();
    }

    public void EnableShooting(bool enable)
    {
        isShootingEnabled = enable;
    }

    public void ResetShootingState()
    {
        isShootingEnabled = true;
    }

    public void TryShoot()
    {
        if (!IsOwner) return;
        if (!isShootingEnabled) return;

        if (arrowPrefab == null || firePoint == null)
        {
            Debug.LogError("Nincs beállítva az ArrowPrefab vagy a FirePoint!");
            return;
        }

        if (muzzleFlash != null) muzzleFlash.Play();

        Vector3 aimDir = GetAimDirection();

        // Átadjuk a saját ID-nkat is (OwnerClientId)
        SpawnArrowServerRpc(firePoint.position, aimDir, NetworkObjectId);
    }

    private Vector3 GetAimDirection()
    {
        Camera cam = Camera.main;
        if (cam == null) return firePoint.forward;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));

        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            return (hit.point - firePoint.position).normalized;
        }
        else
        {
            return (ray.GetPoint(100f) - firePoint.position).normalized;
        }
    }

    // [MODIFIED] Új paraméter: shooterId
    [ServerRpc]
    private void SpawnArrowServerRpc(Vector3 spawnPos, Vector3 direction, ulong shooterObjectId)
    {
        GameObject arrowInstance = Instantiate(arrowPrefab, spawnPos, Quaternion.LookRotation(direction));

        var netObj = arrowInstance.GetComponent<NetworkObject>();
        netObj.Spawn();

        var arrowScript = arrowInstance.GetComponent<ArrowProjectile>();
        if (arrowScript != null)
        {
            // Átadjuk az Object ID-t
            arrowScript.Initialize(shooterObjectId);
        }

        Rigidbody arrowRb = arrowInstance.GetComponent<Rigidbody>();
        if (arrowRb != null)
        {
            arrowRb.linearVelocity = direction * shootForce;
        }
    }
}