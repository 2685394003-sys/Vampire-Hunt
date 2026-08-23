using UnityEngine;
using VampireHunt.Enemies;
using VampireHunt.Infrastructure.Netcode;

namespace VampireHunt.Presentation.Enemies
{
    /// <summary>Presentation-only consumer of replicated enemy state.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyPresenter : MonoBehaviour
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

        private MaterialPropertyBlock m_PropertyBlock;
        private uint m_LastAttackSequence;

        private void Awake()
        {
            m_PropertyBlock = new MaterialPropertyBlock();
            if (targetRenderer == null) targetRenderer = GetComponentInChildren<Renderer>();
        }

        public void Apply(in EnemyNetworkState state)
        {
            bool isTelegraphing = state.State == EnemyState.Telegraphing;
            bool isDead = state.State == EnemyState.Dead;
            // UnityEngine.Object uses a custom null comparison. The null-
            // conditional operator bypasses it and can throw for an unassigned
            // optional serialized reference.
            if (telegraphVisual != null)
            {
                telegraphVisual.SetActive(isTelegraphing);
            }
            ApplyColor(isTelegraphing ? telegraphColor : normalColor);

            if (animator == null) return;

            animator.SetInteger(s_StateId, (int)state.State);
            animator.SetBool(s_DeadId, isDead);
            animator.SetFloat(s_HealthNormalizedId, state.MaxHealth > 0f ? state.CurrentHealth / state.MaxHealth : 0f);
            if (state.AttackSequence > m_LastAttackSequence)
            {
                m_LastAttackSequence = state.AttackSequence;
                animator.SetTrigger(s_AttackTriggerId);
            }
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
