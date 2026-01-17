using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(AudioSource))]
public class PlayerFootstepSystem : NetworkBehaviour
{
    [Header("Beállítások")]
    [SerializeField] private AudioClip[] stepSounds;
    [SerializeField] private float walkInterval = 0.5f;
    [SerializeField] private float runInterval = 0.3f;
    [SerializeField] private float velocityThreshold = 0.1f; // Mikor számít mozgásnak

    private CharacterController characterController;
    private AudioSource audioSource;
    private float nextStepTime;

    public override void OnNetworkSpawn()
    {
        characterController = GetComponent<CharacterController>();
        audioSource = GetComponent<AudioSource>();

        // Fontos: A hangot mindenki hallja, de a logikát csak a tulajdonos vagy a szerver futtatja?
        // Jobb, ha minden kliens maga számolja a lépést a látott mozgás alapján (Client-side prediction),
        // így nem terheljük a hálózatot RPC-kkel minden lépésnél.
    }

    private void Update()
    {
        // Minden kliensen futtatjuk! Ha látjuk, hogy a karakter mozog, lejátszuk a hangot.
        if (characterController == null) return;

        // Csak akkor lépünk, ha a földön van és mozog
        if (characterController.isGrounded && characterController.velocity.magnitude > velocityThreshold)
        {
            float currentInterval = IsRunning() ? runInterval : walkInterval;

            if (Time.time >= nextStepTime)
            {
                PlayStepSound();
                nextStepTime = Time.time + currentInterval;
            }
        }
    }

    private bool IsRunning()
    {
        // Egyszerû becslés: ha a sebesség nagyobb mint a séta fele + futás fele átlaga
        return characterController.velocity.magnitude > 6f;
    }

    private void PlayStepSound()
    {
        if (stepSounds.Length == 0 || audioSource == null) return;

        // Random hang a listából
        AudioClip clip = stepSounds[Random.Range(0, stepSounds.Length)];

        // Random pitch, hogy ne legyen gépies
        audioSource.pitch = Random.Range(0.9f, 1.1f);
        audioSource.PlayOneShot(clip);
    }
}