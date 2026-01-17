using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkAnimator))]
public class DeerAIController : NetworkBehaviour
{
    [Header("Beállítások")]
    [SerializeField] private float wanderRadius = 15f; // Milyen messze mehet el
    [SerializeField] private float waitTimeMin = 2f;   // Mennyit várjon egy helyben
    [SerializeField] private float waitTimeMax = 5f;
    [SerializeField] private float walkSpeed = 3.5f;

    [Header("Animáció")]
    // Fontos: Ezek a nevek egyezzenek meg a Player Animator paramétereivel!
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string eatTrigger = "Eat";

    private NavMeshAgent agent;
    private Animator animator;
    private float timer;
    private bool isEating = false;

    public override void OnNetworkSpawn()
    {
        // Csak a Szerver futtatja az AI logikát!
        // A kliensek csak a NetworkTransform és NetworkAnimator miatt látják az eredményt.
        if (!IsServer)
        {
            enabled = false; // Kliensen kikapcsoljuk az Update-et
            return;
        }

        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        agent.speed = walkSpeed;
        timer = Random.Range(waitTimeMin, waitTimeMax);

        // Azonnal induljon el valahova
        SetRandomDestination();
    }

    private void Update()
    {
        if (!IsServer) return;

        // Animáció szinkronizálása a sebességgel
        // (Feltételezzük, hogy a Blend Tree a 'Speed' paramétert figyeli)
        animator.SetFloat(speedParam, agent.velocity.magnitude);

        // Ha épp eszünk, akkor nem mozgunk
        if (isEating) return;

        // Ha elértük a célt (vagy nagyon közel vagyunk)
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (timer > 0)
            {
                timer -= Time.deltaTime;
            }
            else
            {
                // Döntés: Eszünk vagy továbbmegyünk? (50-50%)
                if (Random.value > 0.5f)
                {
                    StartCoroutine(EatRoutine());
                }
                else
                {
                    SetRandomDestination();
                }
            }
        }
    }

    private void SetRandomDestination()
    {
        timer = Random.Range(waitTimeMin, waitTimeMax);

        // Random pont keresése a NavMesh-en
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;

        NavMeshHit hit;
        // Megnézzük, hogy a random pont érvényes-e a NavMesh-en
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, 1))
        {
            agent.SetDestination(hit.position);
        }
    }

    private System.Collections.IEnumerator EatRoutine()
    {
        isEating = true;
        agent.isStopped = true; // Megáll

        // Animáció triggerelése
        animator.SetTrigger(eatTrigger);

        // Várakozás amíg "eszik" (pl. 3 mp)
        yield return new WaitForSeconds(3f);

        agent.isStopped = false; // Indulás
        isEating = false;

        // Új célpont
        SetRandomDestination();
    }
}