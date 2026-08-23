using System;
using System.Collections.Generic;
using VampireHunt.Contracts;

namespace VampireHunt.Combat
{
    /// <summary>Deterministic, allocation-free evaluation of registered attribute modifiers.</summary>
    public sealed class AttributeModifierCollection
    {
        private readonly List<IAttributeModifier> m_Modifiers = new List<IAttributeModifier>();

        public int Count => m_Modifiers.Count;

        public bool Register(IAttributeModifier modifier)
        {
            if (modifier == null || m_Modifiers.Contains(modifier)) return false;
            m_Modifiers.Add(modifier);
            return true;
        }

        public bool Unregister(IAttributeModifier modifier) =>
            modifier != null && m_Modifiers.Remove(modifier);

        public float Resolve(int attributeId, float baseValue)
        {
            float flat = 0f;
            float additivePercent = 0f;
            float multiplicative = 1f;

            for (int i = 0; i < m_Modifiers.Count; i++)
            {
                IAttributeModifier modifier = m_Modifiers[i];
                if (modifier.AttributeId != attributeId) continue;
                float value = modifier.Value;
                if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                switch (modifier.Operation)
                {
                    case AttributeModifierOperation.Flat:
                        flat += value;
                        break;
                    case AttributeModifierOperation.AdditivePercent:
                        additivePercent += value;
                        break;
                    case AttributeModifierOperation.Multiplicative:
                        multiplicative *= Math.Max(0f, 1f + value);
                        break;
                }
            }

            return (baseValue + flat) * Math.Max(0f, 1f + additivePercent) * multiplicative;
        }

        public void Clear() => m_Modifiers.Clear();
    }
}
