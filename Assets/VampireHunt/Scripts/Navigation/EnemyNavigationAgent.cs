using UnityEngine;
using UnityEngine.AI;

namespace VampireHunt.Navigation
{
    /// <summary>
    /// Server-side NavMesh steering adapter. The NavMeshAgent calculates a path
    /// and avoidance velocity while the CharacterController remains the only
    /// component allowed to move the enemy transform.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class EnemyNavigationAgent : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent agent;
        [Min(0.05f)] [SerializeField] private float repathInterval = 0.25f;
        [Min(0.05f)] [SerializeField] private float destinationMoveThreshold = 0.5f;
        [Min(0.1f)] [SerializeField] private float navMeshSampleRadius = 1.5f;

        private Vector3 m_LastDestination;
        private float m_NextRepathTime;
        private bool m_HasDestination;
        private bool m_ServerActive;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            ConfigureAgent();
        }

        public void SetServerActive(bool active)
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            m_ServerActive = active;
            if (agent == null) return;

            ConfigureAgent();
            agent.enabled = active;
            ResetRuntime();
        }

        public void ResetRuntime()
        {
            m_HasDestination = false;
            m_NextRepathTime = 0f;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
            agent.nextPosition = transform.position;
        }

        public bool TryGetDesiredVelocity(
            Vector3 destination,
            float moveSpeed,
            float stoppingDistance,
            out Vector3 desiredVelocity)
        {
            desiredVelocity = Vector3.zero;
            if (!m_ServerActive || agent == null || !agent.enabled || !TryEnsureOnNavMesh()) return false;

            agent.nextPosition = transform.position;
            agent.speed = Mathf.Max(0.01f, moveSpeed);
            agent.stoppingDistance = Mathf.Max(0f, stoppingDistance);
            agent.isStopped = false;

            float destinationThresholdSqr = destinationMoveThreshold * destinationMoveThreshold;
            bool destinationChanged = !m_HasDestination ||
                                      (destination - m_LastDestination).sqrMagnitude >= destinationThresholdSqr;
            if (destinationChanged || Time.unscaledTime >= m_NextRepathTime ||
                (!agent.pathPending && !agent.hasPath))
            {
                if (!agent.SetDestination(destination))
                {
                    m_HasDestination = false;
                    return false;
                }

                m_LastDestination = destination;
                m_NextRepathTime = Time.unscaledTime + repathInterval;
                m_HasDestination = true;
            }

            if (agent.pathPending) return true;
            if (agent.pathStatus == NavMeshPathStatus.PathInvalid) return false;
            if (agent.hasPath && agent.remainingDistance <= agent.stoppingDistance + 0.05f) return true;

            desiredVelocity = agent.desiredVelocity;
            desiredVelocity.y = 0f;
            desiredVelocity = Vector3.ClampMagnitude(desiredVelocity, moveSpeed);
            return true;
        }

        public void Stop(bool clearPath = false)
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            agent.nextPosition = transform.position;
            agent.isStopped = true;
            if (!clearPath) return;
            agent.ResetPath();
            m_HasDestination = false;
        }

        public void SyncToTransform()
        {
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            agent.nextPosition = transform.position;
        }

        private void ConfigureAgent()
        {
            if (agent == null) return;
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.autoRepath = true;
            agent.autoBraking = true;
        }

        private bool TryEnsureOnNavMesh()
        {
            if (agent.isOnNavMesh) return true;
            if (!NavMesh.SamplePosition(
                    transform.position,
                    out NavMeshHit hit,
                    navMeshSampleRadius,
                    agent.areaMask)) return false;

            return agent.Warp(hit.position);
        }
    }
}
