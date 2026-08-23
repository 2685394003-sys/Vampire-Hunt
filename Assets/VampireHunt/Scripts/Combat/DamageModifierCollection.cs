using System.Collections.Generic;
using VampireHunt.Contracts;

namespace VampireHunt.Combat
{
    /// <summary>
    /// Deterministic priority-ordered modifier pipeline. It is intentionally
    /// unaware of the source of a modifier so Progression can contribute pacts.
    /// </summary>
    public sealed class DamageModifierCollection
    {
        private readonly List<IDamageModifier> m_Modifiers = new List<IDamageModifier>();

        public int Count => m_Modifiers.Count;

        public bool Register(IDamageModifier modifier)
        {
            if (modifier == null || m_Modifiers.Contains(modifier)) return false;

            // Insert after existing modifiers with the same priority. This keeps
            // pact resolution deterministic while preserving registration order
            // for otherwise-equal rules.
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

        public bool Unregister(IDamageModifier modifier)
        {
            return modifier != null && m_Modifiers.Remove(modifier);
        }

        public ResolvedDamage Resolve(in DamageRequest request)
        {
            var context = new DamageContext(request);
            for (int i = 0; i < m_Modifiers.Count; i++)
            {
                m_Modifiers[i].Modify(context);
                if (context.IsCancelled) break;
            }

            return context.ToResult();
        }

        public void Clear()
        {
            m_Modifiers.Clear();
        }
    }
}
