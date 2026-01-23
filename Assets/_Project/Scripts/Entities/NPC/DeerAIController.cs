using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Netcode.Components;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(NetworkAnimator))]
public class DeerAIController : NetworkBehaviour
{
    [Header("Be�ll�t�sok")]
    [SerializeField] private float wanderRadius = 15f;
    [SerializeField] private float waitTimeMin = 2f;
    [SerializeField] private float waitTimeMax = 5f;
    [SerializeField] private float walkSpeed = 3.5f;

    [Header("Ev�s Be�ll�t�sok")]
    [SerializeField] private float eatTimeMin = 4f;
    [SerializeField] private float eatTimeMax = 10f;

    [Header("Anim�ci�")]
    [SerializeField] private string speedParam = "Speed";
    // --- M�DOS�T�S KEZDETE ---
    // Trigger helyett Bool param�tert haszn�lunk a folyamatos �llapothoz
    [SerializeField] private string eatBool = "Eat";
    // --- M�DOS�T�S V�GE ---

    private NavMeshAgent agent;
    private Animator animator;
    private float timer;
    private bool isEating = false;
    private HealthComponent healthComponent;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }

        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        healthComponent = GetComponent<HealthComponent>();

        agent.speed = walkSpeed;
        timer = Random.Range(waitTimeMin, waitTimeMax);

        SetRandomDestination();
    }

    private void Update()
    {
        if (!IsServer) return;

        // [NEW] NPC halál ellenőrzése
        if (healthComponent != null && healthComponent.currentHealth.Value <= 0)
        {
            if (GetComponent<NetworkObject>() != null)
            {
                GetComponent<NetworkObject>().Despawn(false);
            }
            return;
        }

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

        // --- M�DOS�T�S KEZDETE ---
        // Bekapcsoljuk az ev�s �llapotot (Bool = true)
        animator.SetBool(eatBool, true);
        // --- M�DOS�T�S V�GE ---

        float currentEatTime = Random.Range(eatTimeMin, eatTimeMax);

        yield return new WaitForSeconds(currentEatTime);

        // --- M�DOS�T�S KEZDETE ---
        // Letelt az id�, kikapcsoljuk az ev�st (Bool = false)
        animator.SetBool(eatBool, false);
        // --- M�DOS�T�S V�GE ---

        agent.isStopped = false;
        isEating = false;

        SetRandomDestination();
    }
}