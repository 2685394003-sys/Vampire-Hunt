using System.Collections.Generic;
using VampireHunt.Contracts;

namespace VampireHunt.Combat
{
    public sealed class AbilityModifierCollection
    {
        private readonly List<IAbilityCastModifier> m_Modifiers = new List<IAbilityCastModifier>();

        public bool Register(IAbilityCastModifier modifier)
        {
            if (modifier == null || m_Modifiers.Contains(modifier)) return false;

            int insertIndex = m_Modifiers.Count;
            for (int i = 0; i < m_Modifiers.Count; i++)
            {
                if (modifier.Priority >= m_Modifiers[i].Priority) continue;
                insertIndex = i;
                break;
            }

            m_Modifiers.Insert(insertIndex, modifier);
            return true;
        }

        public bool Unregister(IAbilityCastModifier modifier) =>
            modifier != null && m_Modifiers.Remove(modifier);

        public void Resolve(AbilityCastPlan plan)
        {
            if (plan == null) return;
            for (int i = 0; i < m_Modifiers.Count && !plan.IsCancelled; i++)
            {
                m_Modifiers[i].Modify(plan);
            }
        }

        public void Clear() => m_Modifiers.Clear();
    }
}
