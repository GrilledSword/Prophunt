using Unity.Netcode;
using UnityEngine;

public class HealthComponent : NetworkBehaviour
{
    // Init: 100 HP.
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Beállítások")]
    [SerializeField] public bool isHunter = false; // Ezt a PlayerController állítja majd be
    [SerializeField] private float decayRate = 1.0f; // Mennyi élet fogy másodpercenként?
    [SerializeField] private float lowHealthThreshold = 20f; // Mikor kezdjen el "kiabálni/fingani"?

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lowHealthSound; // A "Fingás" vagy lihegés hangja
    private float nextSoundTime = 0f;

    public delegate void DeathEvent(ulong clientId);
    public event DeathEvent OnDeath;

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += (oldVal, newVal) =>
        {
            // UI frissítéshez event (opcionális, de hasznos debug)
            // Debug.Log($"[Health] {name} HP: {newVal}");
        };
    }

    private void Update()
    {
        if (!IsServer) return;

        // 1. Folyamatos életvesztés (Mindenkinek)
        if (currentHealth.Value > 0)
        {
            ModifyHealth(-decayRate * Time.deltaTime);
        }

        // 2. Hangjelzés, ha kevés az élet (Csak Szarvasnak, vagy mindkettõnek?)
        // A kérésed szerint: "szarvasnak... valami hallható hangot adjon ki"
        if (!isHunter && currentHealth.Value <= lowHealthThreshold && currentHealth.Value > 0)
        {
            if (Time.time >= nextSoundTime)
            {
                PlayLowHealthSoundClientRpc();
                nextSoundTime = Time.time + Random.Range(3f, 8f); // Véletlenszerû idõközönként
            }
        }
    }

    // Univerzális gyógyítás/sebzés metódus
    public void ModifyHealth(float amount)
    {
        if (!IsServer) return;

        float newValue = currentHealth.Value + amount;
        currentHealth.Value = Mathf.Clamp(newValue, 0, 100);

        if (currentHealth.Value <= 0)
        {
            Die();
        }
    }

    // A fegyver ezt hívja majd. A szarvasnál az "amount" irreleváns, mert INSTANT halál van.
    public void TakeHit(float amount)
    {
        if (!IsServer) return;

        if (!isHunter)
        {
            // SZARVAS LOGIKA: Bármilyen találat -> Instant Halál
            currentHealth.Value = 0;
            Die();
        }
        else
        {
            // VADÁSZ LOGIKA: Normál sebzõdés (pl. sprinttõl vagy büntetésbõl)
            ModifyHealth(-amount);
        }
    }

    private void Die()
    {
        // [MODIFIED]

        // Nem semmisítjük meg azonnal, hanem szólunk a Managernek
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.OnPlayerDied(OwnerClientId, isHunter);
        }

        // Ha Szarvas, akkor Despawnolhat (vagyis a vizuál eltûnik, de a Ghost mód aktív marad a kliensen)
        // A PlayerController kezeli a ghost módot, itt csak a "testet" tüntetjük el.
        if (!isHunter)
        {
            // Fontos: Ne Despawnoljuk teljesen a NetworkObjectet, mert akkor a Kliens kapcsolata is megszakad a karakterrel!
            // Helyette csak rejtsük el a szerveren mindenki elõl.
            HidePlayerServerRpc();
        }
        else
        {
            // Hunter pánik esetén NEM hal meg, hanem tovább él (0 HP-val vagy újratöltve kicsit)
            // Adjunk neki egy kis "adrenalin" életet a futáshoz
            currentHealth.Value = 100f;
        }
    }

    [ClientRpc]
    private void PlayLowHealthSoundClientRpc()
    {
        if (audioSource != null && lowHealthSound != null)
        {
            audioSource.PlayOneShot(lowHealthSound);
        }
    }
    [ServerRpc]
    private void HidePlayerServerRpc()
    {
        // Kikapcsoljuk a hit boxot és a láthatóságot, de a NetworkObject marad!
        GetComponent<Collider>().enabled = false;
        // A vizuális elrejtést a PlayerController GhostModeClientRpc-je intézi
    }
}