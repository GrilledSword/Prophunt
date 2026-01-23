using Unity.Netcode;
using UnityEngine;

public class HealthComponent : NetworkBehaviour
{
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Be�ll�t�sok")]
    [SerializeField] public bool isHunter = false;
    [SerializeField] private float decayRate = 0.5f;
    [SerializeField] private float lowHealthThreshold = 20f;
    [SerializeField] private float deerPanicDamagePerHit = 25f; // Szarvas sebzése Panic módban

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lowHealthSound;
    private float nextSoundTime = 0f;

    public NetworkVariable<bool> isDraining = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> isPanicModeActive = new NetworkVariable<bool>(false); // Panic mode flag
    public delegate void DeathEvent(ulong clientId, bool wasHunter, bool isInstaKill);
    //public event DeathEvent OnDeath;

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
        if (!IsServer || NetworkGameManager.Instance == null) return;

        bool isGameActive = NetworkGameManager.Instance.IsInGame();
        bool isReleasePhase = NetworkGameManager.Instance.currentGameState.Value == NetworkGameManager.GameState.HunterRelease;
        bool isPanicMode = isPanicModeActive.Value;

        // Csak InGame state-ben fogyjon, és NEM pánik módban
        if (isGameActive && !isReleasePhase && !isPanicMode && currentHealth.Value > 0)
        {
            ModifyHealth(-decayRate * Time.deltaTime);
        }

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
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SetPanicModeActiveRpc(bool active)
    {
        isPanicModeActive.Value = active;
    }
    public void DeerAttackHunter(ulong hunterClientId)
    {
        if (!IsServer) return;
        if (isPanicModeActive.Value && !isHunter)
        {
            // Szarvas sebez egy vadászt panic módban
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(NetworkObjectId, out NetworkObject deerObj))
            {
                var hunterHealth = deerObj.GetComponent<HealthComponent>();
                if (hunterHealth != null && hunterHealth.isHunter && hunterHealth.currentHealth.Value > 0)
                {
                    hunterHealth.ModifyHealth(-deerPanicDamagePerHit);
                    Debug.Log($"[HealthComponent] Szarvas megtámadott egy vadászt! Sebzés: {deerPanicDamagePerHit}");
                }
            }
        }
    }
}