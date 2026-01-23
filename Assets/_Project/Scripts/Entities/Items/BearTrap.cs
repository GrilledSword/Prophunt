using Unity.Netcode;
using UnityEngine;

public class BearTrap : NetworkBehaviour
{
    private NetworkVariable<bool> isActivated = new NetworkVariable<bool>(false);
    private Animator animator;
    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }
    public override void OnNetworkSpawn()
    {
        // [ÚJ] Amikor létrejön a hálózaton, ellenõrizzük az állapotot
        isActivated.OnValueChanged += OnTrapStateChanged;

        // Ha már aktiválva van (pl. late join), azonnal frissítsük a vizuált
        if (isActivated.Value)
        {
            SetTrapVisuals(true);
        }
    }
    public override void OnNetworkDespawn()
    {
        isActivated.OnValueChanged -= OnTrapStateChanged;
    }
    private void OnTrapStateChanged(bool previous, bool current)
    {
        SetTrapVisuals(current);
    }
    private void SetTrapVisuals(bool activated)
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null && activated)
        {
            animator.Play("ClosedState");
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (isActivated.Value) return; // Már be van csukva

        if (NetworkGameManager.Instance != null && !NetworkGameManager.Instance.areTrapsActive.Value)
        {
            return;
        }

        var victimController = other.GetComponentInParent<PlayerNetworkController>();
        if (victimController != null)
        {
            // Itt csak az adatot állítjuk, a OnValueChanged esemény kezeli a vizuált mindenkinél!
            isActivated.Value = true;

            victimController.SetTrappedClientRpc(true);

            // Nem kell külön RPC, a Variable change elég!
            // CloseTrapClientRpc(); <-- EZT TÖRÖLHETED
        }
    }
}