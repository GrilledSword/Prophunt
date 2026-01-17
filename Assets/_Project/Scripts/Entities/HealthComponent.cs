using Unity.Netcode;
using UnityEngine;

public class HealthComponent : NetworkBehaviour
{
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Beállítások")]
    [SerializeField] public bool isHunter = false;
    [SerializeField] private float decayRate = 0.5f;
    [SerializeField] private float lowHealthThreshold = 20f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lowHealthSound;
    private float nextSoundTime = 0f;

    public NetworkVariable<bool> isDraining = new NetworkVariable<bool>(false);
    public delegate void DeathEvent(ulong clientId, bool wasHunter, bool isInstaKill);
    public event DeathEvent OnDeath;

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += OnHealthChanged;
    }
    private void OnHealthChanged(float oldVal, float newVal)
    {
        if (GameHUD.Instance == null) return;

        if (IsOwner)
        {
            GameHUD.Instance.UpdateMyHealth(newVal);
        }

        var player = GetComponent<PlayerNetworkController>();

        if (player != null && player.isHunter.Value)
        {
            GameHUD.Instance.UpdateHunterSanity(newVal);
        }
    }
    private void Update()
    {
        if (!IsServer) return;
        if (NetworkGameManager.Instance == null) return;

        bool isGameActive = NetworkGameManager.Instance.IsInGame();
        bool isReleasePhase = false;
        if (GameLoopManager.Instance != null)
        {
            isReleasePhase = NetworkGameManager.Instance.currentGameState.Value == NetworkGameManager.GameState.HunterRelease;
        }

        if (isGameActive && !isReleasePhase && currentHealth.Value > 0)
        {
            ModifyHealth(-decayRate * Time.deltaTime);
        }
        else { }

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
        if (currentHealth.Value <= 0) return;

        float newValue = currentHealth.Value + amount;
        currentHealth.Value = Mathf.Clamp(newValue, 0, 100);

        if (currentHealth.Value <= 0)
        {
            Die(false);
        }
    }
    public void TakeHit(float amount, bool isInstaKill = false)
    {
        if (!IsServer) return;

        if (!isHunter)
        {
            currentHealth.Value = 0;
            Die(true);
        }
        else
        {
            if (isInstaKill)
            {
                currentHealth.Value = 0;
                Die(true);
            }
            else
            {
                ModifyHealth(-amount);
            }
        }
    }
    private void Die(bool isInstaKill)
    {
        if (NetworkGameManager.Instance != null)
        {
            NetworkGameManager.Instance.OnPlayerDied(OwnerClientId, isHunter, isInstaKill);
        }

        if (!isHunter || isInstaKill)
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }
        else
        {
            currentHealth.Value = 100f;
        }
    }
    [ClientRpc]
    private void PlayLowHealthSoundClientRpc()
    {
        if (audioSource != null && lowHealthSound != null) audioSource.PlayOneShot(lowHealthSound);
    }
}