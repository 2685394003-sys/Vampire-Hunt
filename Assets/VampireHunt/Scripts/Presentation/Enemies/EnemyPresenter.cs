using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Enemies;

namespace VampireHunt.Presentation.Enemies
{
    /// <summary>Presentation-only consumer of replicated enemy state.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyPresenter : MonoBehaviour, IEnemyPresentationSink
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private GameObject telegraphVisual;
        [SerializeField] private Color normalColor = new Color(0.35f, 0.08f, 0.08f, 1f);
        [SerializeField] private Color telegraphColor = new Color(1f, 0.25f, 0.05f, 1f);

        private static readonly int s_StateId = Animator.StringToHash("EnemyState");
        private static readonly int s_HealthNormalizedId = Animator.StringToHash("HealthNormalized");
        private static readonly int s_AttackTriggerId = Animator.StringToHash("Attack");
        private static readonly int s_DeadId = Animator.StringToHash("Dead");
        private static readonly int s_MoveSpeedId = Animator.StringToHash("MoveSpeed");
        private static readonly int s_LegacyMoveId = Animator.StringToHash("move");

        private MaterialPropertyBlock m_PropertyBlock;
        private uint m_LastAttackSequence;
        private EnemyState m_CurrentState;
        private Vector3 m_LastPosition;
        private bool m_PositionInitialized;
        private bool m_HasState;
        private bool m_HasHealthNormalized;
        private bool m_HasDead;
        private bool m_HasMoveSpeed;
        private bool m_HasLegacyMove;
        private bool m_HasAttack;
        private AnimatorControllerParameterType m_AttackParameterType;

        private void Awake()
        {
            m_PropertyBlock = new MaterialPropertyBlock();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
            CacheAnimatorParameters();
            ResetMotionSample();
        }

        private void OnEnable()
        {
            ResetMotionSample();
        }

        private void OnDisable()
        {
            if (animator != null && m_HasAttack && m_AttackParameterType == AnimatorControllerParameterType.Bool)
                animator.SetBool(s_AttackTriggerId, false);
        }

        private void Update()
        {
            if (animator == null) return;

            Vector3 position = transform.position;
            float speed = 0f;
            if (m_PositionInitialized && Time.deltaTime > 0.0001f && m_CurrentState != EnemyState.Dead)
            {
                Vector3 delta = position - m_LastPosition;
                delta.y = 0f;
                speed = delta.magnitude / Time.deltaTime;
            }
            m_LastPosition = position;
            m_PositionInitialized = true;

            if (m_HasMoveSpeed)
                animator.SetFloat(s_MoveSpeedId, speed, 0.1f, Time.deltaTime);
            if (m_HasLegacyMove)
                animator.SetFloat(s_LegacyMoveId, speed, 0.1f, Time.deltaTime);
        }

        public void Apply(in EnemyPresentationState state)
        {
            EnemyState enemyState = (EnemyState)state.State;
            bool isTelegraphing = enemyState == EnemyState.Telegraphing;
            bool isDead = enemyState == EnemyState.Dead;
            m_CurrentState = enemyState;
            // UnityEngine.Object uses a custom null comparison. The null-
            // conditional operator bypasses it and can throw for an unassigned
            // optional serialized reference.
            if (telegraphVisual != null)
            {
                telegraphVisual.SetActive(isTelegraphing);
            }
            ApplyColor(isTelegraphing ? telegraphColor : normalColor);

            if (animator == null) return;

            if (m_HasState) animator.SetInteger(s_StateId, state.State);
            if (m_HasDead) animator.SetBool(s_DeadId, isDead);
            if (m_HasHealthNormalized)
                animator.SetFloat(s_HealthNormalizedId, state.MaxHealth > 0f ? state.CurrentHealth / state.MaxHealth : 0f);

            if (m_HasAttack && m_AttackParameterType == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(s_AttackTriggerId, enemyState == EnemyState.Attacking);
            }
            else if (m_HasAttack && state.AttackSequence > m_LastAttackSequence)
            {
                animator.SetTrigger(s_AttackTriggerId);
            }
            m_LastAttackSequence = state.AttackSequence;
        }

        private void CacheAnimatorParameters()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter.nameHash == s_StateId && parameter.type == AnimatorControllerParameterType.Int)
                    m_HasState = true;
                else if (parameter.nameHash == s_HealthNormalizedId && parameter.type == AnimatorControllerParameterType.Float)
                    m_HasHealthNormalized = true;
                else if (parameter.nameHash == s_DeadId && parameter.type == AnimatorControllerParameterType.Bool)
                    m_HasDead = true;
                else if (parameter.nameHash == s_MoveSpeedId && parameter.type == AnimatorControllerParameterType.Float)
                    m_HasMoveSpeed = true;
                else if (parameter.nameHash == s_LegacyMoveId && parameter.type == AnimatorControllerParameterType.Float)
                    m_HasLegacyMove = true;
                else if (parameter.nameHash == s_AttackTriggerId &&
                         (parameter.type == AnimatorControllerParameterType.Trigger ||
                          parameter.type == AnimatorControllerParameterType.Bool))
                {
                    m_HasAttack = true;
                    m_AttackParameterType = parameter.type;
                }
            }
        }

        private void ResetMotionSample()
        {
            m_LastPosition = transform.position;
            m_PositionInitialized = true;
        }

        private void ApplyColor(Color color)
        {
            if (targetRenderer == null) return;
            targetRenderer.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor("_BaseColor", color);
            m_PropertyBlock.SetColor("_Color", color);
            m_PropertyBlock.SetColor("_EmissionColor", color * 2f);
            targetRenderer.SetPropertyBlock(m_PropertyBlock);
        }
    }
}
