using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    /// <summary>
    /// Samples the current target and Boss health into one immutable ability input.
    /// Chase AI can replace the target at runtime without knowing about ability scheduling.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAbilityContextProvider : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [Tooltip("Keeps the demo operational until the real multiplayer target sensor is connected.")]
        [SerializeField] private bool useForwardPreviewWhenTargetIsMissing = true;
        [Min(0f)] [SerializeField] private float previewTargetDistance = 5f;

        [Header("Boss State Adapter")]
        [Tooltip("Temporary adapter value. The Boss vitals component will supply this later.")]
        [Range(0f, 1f)] [SerializeField] private float normalizedBossHealth = 1f;

        private IPlayerTargetQuery m_PlayerTargetQuery;
        private IBossBodyState m_BossBodyState;
        private IBossBodyStateControl m_BossBodyStateControl;

        public Transform Target => target;

        private void Awake()
        {
            ResolveServicePorts();
        }

        public BossAbilityExecutionInput Capture()
        {
            ResolveServicePorts();
            bool hasTarget = target != null;
            Vector3 targetPosition;
            ulong targetEntityId = 0;

            if (target != null)
            {
                targetPosition = target.position;
                targetEntityId = ResolveCombatEntityId(target).Value;
            }
            else if (m_PlayerTargetQuery != null && m_PlayerTargetQuery.TryGetNearest(
                         ToFloat3(transform.position),
                         float.MaxValue,
                         out BossPlayerTarget playerTarget))
            {
                hasTarget = true;
                targetPosition = ToVector3(playerTarget.Position);
                targetEntityId = playerTarget.EntityId.Value;
            }
            else
            {
                hasTarget = useForwardPreviewWhenTargetIsMissing;
                targetPosition = transform.position + transform.forward * previewTargetDistance;
            }

            Vector3 offset = targetPosition - transform.position;
            float distance = hasTarget ? offset.magnitude : 0f;
            Vector3 direction = offset.sqrMagnitude > 0.0001f
                ? offset.normalized
                : transform.forward;
            var selection = new BossAbilitySelectionContext(
                distance,
                m_BossBodyState?.NormalizedHealth ?? normalizedBossHealth,
                hasTarget);

            return new BossAbilityExecutionInput(
                selection,
                targetEntityId,
                ToFloat3(transform.position),
                ToFloat3(targetPosition),
                ToFloat3(direction));
        }

        public void SetTargetServer(Transform value)
        {
            target = value;
        }

        public void SetNormalizedBossHealthServer(float value)
        {
            normalizedBossHealth = Mathf.Clamp01(value);
            ResolveServicePorts();
            m_BossBodyStateControl?.TrySetNormalizedHealth(normalizedBossHealth);
        }

        private void OnValidate()
        {
            previewTargetDistance = Mathf.Max(0f, previewTargetDistance);
            normalizedBossHealth = Mathf.Clamp01(normalizedBossHealth);
        }

        private void ResolveServicePorts()
        {
            if (m_PlayerTargetQuery != null && m_BossBodyState != null && m_BossBodyStateControl != null) return;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (m_PlayerTargetQuery == null && behaviours[i] is IPlayerTargetQuery targets)
                    m_PlayerTargetQuery = targets;
                if (m_BossBodyState == null && behaviours[i] is IBossBodyState bodyState)
                    m_BossBodyState = bodyState;
                if (m_BossBodyStateControl == null && behaviours[i] is IBossBodyStateControl bodyStateControl)
                    m_BossBodyStateControl = bodyStateControl;
            }
        }

        private static GameplayEntityId ResolveCombatEntityId(Transform value)
        {
            MonoBehaviour[] behaviours = value.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is ICombatEntityIdentity identity) return identity.CombatEntityId;

            NetworkObject targetNetworkObject = value.GetComponentInParent<NetworkObject>();
            return targetNetworkObject != null && targetNetworkObject.IsPlayerObject
                ? new GameplayEntityId(targetNetworkObject.OwnerClientId + 1UL)
                : GameplayEntityId.None;
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
