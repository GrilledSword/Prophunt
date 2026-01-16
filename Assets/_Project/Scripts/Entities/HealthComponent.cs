using Unity.Netcode;
using UnityEngine;

public class HealthComponent : NetworkBehaviour
{
    // Init: 100 HP.
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Beállítások")]
    [SerializeField] public bool isHunter = false;
    [SerializeField] private float decayRate = 1.0f;
    [SerializeField] private float lowHealthThreshold = 20f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lowHealthSound;
    private float nextSoundTime = 0f;

    public delegate void DeathEvent(ulong clientId, bool wasHunter, bool isInstaKill); // Bõvített event
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

        // Folyamatos életvesztés (Sanity/Hunger)
        if (currentHealth.Value > 0)
        {
            ModifyHealth(-decayRate * Time.deltaTime);
        }

        // Hangjelzés logika (marad a régi)
        if (!isHunter && currentHealth.Value <= lowHealthThreshold && currentHealth.Value > 0)
        {
            if (Time.time >= nextSoundTime)
            {
                PlayLowHealthSoundClientRpc();
                nextSoundTime = Time.time + Random.Range(3f, 8f);
            }
        }
    }

    public void ModifyHealth(float amount)
    {
        if (!IsServer) return;

        // Ha már halottak vagyunk, ne csináljunk semmit
        if (currentHealth.Value <= 0) return;

        float newValue = currentHealth.Value + amount;
        currentHealth.Value = Mathf.Clamp(newValue, 0, 100);

        if (currentHealth.Value <= 0)
        {
            // Ha elfogyott az élet (pl. Sanity), az NEM instant halál, tehát Panic Mode lehet
            Die(false);
        }
    }

    public void TakeHit(float amount, bool isInstaKill = false)
    {
        if (!IsServer) return;

        if (!isHunter)
        {
            // Szarvas: Mindig instant halál
            currentHealth.Value = 0;
            Die(true); // Szarvasnál mindegy, de legyen true
        }
        else
        {
            // Vadász
            if (isInstaKill)
            {
                currentHealth.Value = 0;
                Die(true); // Taposóakna -> Instant Game Over a vadásznak
            }
            else
            {
                // Sima sebzés (Sanity csökkenés)
                ModifyHealth(-amount);
            }
        }
    }

    private void Die(bool isInstaKill)
    {
        Debug.Log($"[Death] {name} meghalt. Hunter? {isHunter}. InstaKill? {isInstaKill}");

        // Szólunk a Managernek
        if (NetworkGameManager.Instance != null)
        {
            // Itt dõl el, hogy Panic Mode vagy Spectator lesz
            NetworkGameManager.Instance.OnPlayerDied(OwnerClientId, isHunter, isInstaKill);
        }

        // Ha Szarvas VAGY Vadász Instant Halállal -> Eltüntetjük a testet (Spectator)
        if (!isHunter || isInstaKill)
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }
        else
        {
            // Vadász Pánik Mód -> Újratöltjük az életet a futáshoz
            currentHealth.Value = 100f;
        }
    }

    [ClientRpc]
    private void PlayLowHealthSoundClientRpc()
    {
        if (audioSource != null && lowHealthSound != null) audioSource.PlayOneShot(lowHealthSound);
    }
}