using System;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>Optional adapter implemented later by a concrete particle/VFX Graph component.</summary>
    public interface ICombatVfxDriver
    {
        void PlayDamage(in DamagePresentationPayload payload, Transform anchor);
        void ApplyStatus(in StatusEffectPresentationPayload payload, Transform anchor);
        void RemoveStatus(in StatusEffectPresentationPayload payload, Transform anchor);
        void Clear(Transform anchor);
    }

    /// <summary>
    /// Presentation-only combat effect router. It filters global event channels by entity id and
    /// delegates visual work to an optional driver; no gameplay state is modified here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatVfxPresenter : MonoBehaviour
    {
        [Header("Event Channels")]
        [SerializeField] private DamagePresentationEvent onDamage;
        [SerializeField] private StatusEffectPresentationEvent onEffectAdded;
        [SerializeField] private StatusEffectPresentationEvent onEffectRemoved;

        [Header("Future VFX Implementation")]
        [SerializeField] private Transform effectAnchor;
        [Tooltip("Optional MonoBehaviour implementing ICombatVfxDriver.")]
        [SerializeField] private MonoBehaviour vfxDriverBehaviour;

        private ICombatEntityIdentity m_Identity;
        private ICombatVfxDriver m_Driver;

        public event Action<DamagePresentationPayload> DamageVfxRequested;
        public event Action<StatusEffectPresentationPayload> EffectAddedVfxRequested;
        public event Action<StatusEffectPresentationPayload> EffectRemovedVfxRequested;

        private void Awake()
        {
            if (effectAnchor == null) effectAnchor = transform;
            m_Driver = vfxDriverBehaviour as ICombatVfxDriver;
            // 未显式指定 driver 时，回退到同物体上的任意 ICombatVfxDriver 实现（如占位的 StatusEffectVfxDriver）。
            if (m_Driver == null) m_Driver = GetComponent<ICombatVfxDriver>();
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length && m_Identity == null; i++)
            {
                if (behaviours[i] is ICombatEntityIdentity identity) m_Identity = identity;
            }

            if (vfxDriverBehaviour != null && m_Driver == null)
                Debug.LogWarning("[CombatVfxPresenter] Assigned VFX driver does not implement ICombatVfxDriver.", this);
        }

        private void OnEnable()
        {
            onDamage?.RegisterListener(HandleDamage);
            onEffectAdded?.RegisterListener(HandleEffectAdded);
            onEffectRemoved?.RegisterListener(HandleEffectRemoved);
        }

        private void OnDisable()
        {
            onDamage?.UnregisterListener(HandleDamage);
            onEffectAdded?.UnregisterListener(HandleEffectAdded);
            onEffectRemoved?.UnregisterListener(HandleEffectRemoved);
            m_Driver?.Clear(effectAnchor);
        }

        private void HandleDamage(DamagePresentationPayload payload)
        {
            if (!IsForThisEntity(payload.targetEntityId)) return;
            DamageVfxRequested?.Invoke(payload);
            m_Driver?.PlayDamage(payload, effectAnchor);
        }

        private void HandleEffectAdded(StatusEffectPresentationPayload payload)
        {
            if (!IsForThisEntity(payload.targetEntityId)) return;
            EffectAddedVfxRequested?.Invoke(payload);
            m_Driver?.ApplyStatus(payload, effectAnchor);
        }

        private void HandleEffectRemoved(StatusEffectPresentationPayload payload)
        {
            if (!IsForThisEntity(payload.targetEntityId)) return;
            EffectRemovedVfxRequested?.Invoke(payload);
            m_Driver?.RemoveStatus(payload, effectAnchor);
        }

        private bool IsForThisEntity(ulong targetEntityId)
        {
            return targetEntityId != 0 && m_Identity != null &&
                   m_Identity.CombatEntityId.Value == targetEntityId;
        }
    }
}
