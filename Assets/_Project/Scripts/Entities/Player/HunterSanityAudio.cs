using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(AudioSource))]
public class HunterSanityAudio : NetworkBehaviour
{
    [Header("Sanity Audio")]
    [SerializeField] private AudioClip heartbeatClip; // Egy dobbanás hangja (vagy loop)
    [SerializeField] private float startSanityThreshold = 50f; // 50% alatt kezdõdik
    [SerializeField] private float maxVolume = 1.0f;
    [SerializeField] private float maxPitch = 1.5f; // Gyorsulás mértéke

    private HealthComponent healthComponent;
    private AudioSource audioSource;
    private PlayerNetworkController playerController;

    // Ha loop-os heartbeatet használsz
    private bool isHeartbeatPlaying = false;

    public override void OnNetworkSpawn()
    {
        healthComponent = GetComponentInParent<HealthComponent>();
        playerController = GetComponentInParent<PlayerNetworkController>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource != null)
        {
            audioSource.loop = true;
            audioSource.clip = heartbeatClip;
            audioSource.volume = 0;
        }
    }

    private void Update()
    {
        // IsOwner ellenõrzés a szülõn keresztül
        if (playerController == null || !playerController.IsOwner) return;

        // Csak Vadásznak van Sanity effekt
        if (!playerController.isHunter.Value)
        {
            if (audioSource.isPlaying) audioSource.Stop();
            return;
        }

        HandleSanityAudio();
    }

    private void HandleSanityAudio()
    {
        if (healthComponent == null) return;

        float currentSanity = healthComponent.currentHealth.Value;

        if (currentSanity < startSanityThreshold && currentSanity > 0)
        {
            if (!audioSource.isPlaying) audioSource.Play();

            float intensity = 1f - (currentSanity / startSanityThreshold);
            audioSource.volume = Mathf.Lerp(0f, maxVolume, intensity);
            audioSource.pitch = Mathf.Lerp(1f, maxPitch, intensity);
        }
        else
        {
            if (audioSource.volume > 0.01f)
            {
                audioSource.volume = Mathf.Lerp(audioSource.volume, 0f, Time.deltaTime * 2f);
            }
            else
            {
                audioSource.Stop();
            }
        }
    }
}