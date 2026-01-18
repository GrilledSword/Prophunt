using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkAnimator))]
public class DeerAIController : NetworkBehaviour
{
    [Header("Beállítások")]
    [SerializeField] private float wanderRadius = 15f;
    [SerializeField] private float waitTimeMin = 2f;
    [SerializeField] private float waitTimeMax = 5f;
    [SerializeField] private float walkSpeed = 3.5f;

    [Header("Evés Beállítások")]
    [SerializeField] private float eatTimeMin = 4f;
    [SerializeField] private float eatTimeMax = 10f;

    [Header("Animáció")]
    [SerializeField] private string speedParam = "Speed";
    // --- MÓDOSÍTÁS KEZDETE ---
    // Trigger helyett Bool paramétert használunk a folyamatos állapothoz
    [SerializeField] private string eatBool = "Eat";
    // --- MÓDOSÍTÁS VÉGE ---

    private NavMeshAgent agent;
    private Animator animator;
    private float timer;
    private bool isEating = false;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }

        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        agent.speed = walkSpeed;
        timer = Random.Range(waitTimeMin, waitTimeMax);

        SetRandomDestination();
    }

    private void Update()
    {
        if (!IsServer) return;

        animator.SetFloat(speedParam, agent.velocity.magnitude);

        if (isEating) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            if (timer > 0)
            {
                timer -= Time.deltaTime;
            }
            else
            {
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

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, 1))
        {
            agent.SetDestination(hit.position);
        }
    }

    private System.Collections.IEnumerator EatRoutine()
    {
        isEating = true;
        agent.isStopped = true;

        // --- MÓDOSÍTÁS KEZDETE ---
        // Bekapcsoljuk az evés állapotot (Bool = true)
        animator.SetBool(eatBool, true);
        // --- MÓDOSÍTÁS VÉGE ---

        float currentEatTime = Random.Range(eatTimeMin, eatTimeMax);

        yield return new WaitForSeconds(currentEatTime);

        // --- MÓDOSÍTÁS KEZDETE ---
        // Letelt az idõ, kikapcsoljuk az evést (Bool = false)
        animator.SetBool(eatBool, false);
        // --- MÓDOSÍTÁS VÉGE ---

        agent.isStopped = false;
        isEating = false;

        SetRandomDestination();
    }
}