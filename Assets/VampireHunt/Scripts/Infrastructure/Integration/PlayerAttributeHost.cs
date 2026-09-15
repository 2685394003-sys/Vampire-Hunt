using System;
using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Player-owned attribute composition root backed by the template's base stat values.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CoreStatsHandler))]
    public sealed class PlayerAttributeHost : MonoBehaviour, IAttributeModifierTarget
    {
        [SerializeField] private CoreStatsHandler coreStats;
        [SerializeField] private CoreMovement coreMovement;

        private readonly AttributeModifierCollection m_Modifiers = new AttributeModifierCollection();
        private Func<float, float> m_PreviousMoveSpeedResolver;

        public int ModifierCount => m_Modifiers.Count;

        private void Awake()
        {
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
            if (coreMovement == null) coreMovement = GetComponent<CoreMovement>();
            if (coreMovement != null)
            {
                m_PreviousMoveSpeedResolver = coreMovement.MoveSpeedResolver;
                coreMovement.MoveSpeedResolver = ResolveMoveSpeed;
            }
        }

        public bool RegisterAttributeModifier(IAttributeModifier modifier) => m_Modifiers.Register(modifier);
        public bool UnregisterAttributeModifier(IAttributeModifier modifier) => m_Modifiers.Unregister(modifier);

        public float ResolveAttributeValue(int attributeId, float baseValue) =>
            m_Modifiers.Resolve(attributeId, baseValue);

        public float GetFinalAttributeValue(int attributeId)
        {
            float baseValue = coreStats != null ? coreStats.GetCurrentValue(attributeId) : 0f;
            return ResolveAttributeValue(attributeId, baseValue);
        }

        private float ResolveMoveSpeed(float baseValue)
        {
            float input = m_PreviousMoveSpeedResolver != null
                ? m_PreviousMoveSpeedResolver(baseValue)
                : baseValue;
            return ResolveAttributeValue(StatKeys.MoveSpeed, input);
        }

        private void OnDestroy()
        {
            if (coreMovement != null && coreMovement.MoveSpeedResolver == ResolveMoveSpeed)
                coreMovement.MoveSpeedResolver = m_PreviousMoveSpeedResolver;
            m_Modifiers.Clear();
        }
    }
}
