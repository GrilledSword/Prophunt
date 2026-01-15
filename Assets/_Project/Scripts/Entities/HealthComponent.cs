using Unity.Netcode;
using UnityEngine;

public class HealthComponent : NetworkBehaviour
{
    // Hálózati változó az élethez.
    // Init: 100 HP. Csak a Szerver írhatja, de mindenki olvashatja.
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Esemény, ha valaki meghal (feliratkozhat rá a UI, animáció, stb.)
    public delegate void DeathEvent(ulong clientId);
    public event DeathEvent OnDeath;

    public override void OnNetworkSpawn()
    {
        // Ha változik az élet, kiírjuk (Debug) - késõbb ide kötjük a HP csíkot
        currentHealth.OnValueChanged += (oldVal, newVal) =>
        {
            Debug.Log($"[Health] {name} HP: {newVal}");
        };
    }

    // Ezt hívja meg a fegyver, ha eltalál
    public void TakeDamage(int damage)
    {
        // Csak a szerver dönthet a sérülésrõl!
        if (!IsServer) return;

        // Levonjuk az életet
        currentHealth.Value -= damage;

        if (currentHealth.Value <= 0)
        {
            currentHealth.Value = 0;
            Die();
        }
    }

    private void Die()
    {
        Debug.Log($"[Death] {name} meghalt!");

        // Értesítjük a feliratkozókat (pl. GameManager)
        OnDeath?.Invoke(OwnerClientId);

        // MVP Megoldás: Azonnali Respawn vagy Eltûnés
        // Most egyszerûen kikapcsoljuk a karaktert (Spectator mód elõszele)
        // A NetworkObject Despawn-t csak a szerver hívhatja
        GetComponent<NetworkObject>().Despawn(true);
    }
}