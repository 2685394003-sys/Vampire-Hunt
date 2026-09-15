using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Unity composition adapter for outgoing/incoming combat rules. Future
    /// blood-pact executors register domain modifiers through this small port.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatModifierHost : MonoBehaviour, ICombatModifierTarget
    {
        private readonly DamageModifierCollection m_Outgoing = new DamageModifierCollection();
        private readonly DamageModifierCollection m_Incoming = new DamageModifierCollection();
        private readonly List<ICombatOutcomeListener> m_OutcomeListeners = new List<ICombatOutcomeListener>();

        public ResolvedDamage ResolveOutgoing(in DamageRequest request) => m_Outgoing.Resolve(request);
        public ResolvedDamage ResolveIncoming(in DamageRequest request) => m_Incoming.Resolve(request);

        public bool RegisterOutgoingModifier(IDamageModifier modifier) => m_Outgoing.Register(modifier);
        public bool UnregisterOutgoingModifier(IDamageModifier modifier) => m_Outgoing.Unregister(modifier);
        public bool RegisterIncomingModifier(IDamageModifier modifier) => m_Incoming.Register(modifier);
        public bool UnregisterIncomingModifier(IDamageModifier modifier) => m_Incoming.Unregister(modifier);

        public bool RegisterOutcomeListener(ICombatOutcomeListener listener)
        {
            if (listener == null || m_OutcomeListeners.Contains(listener)) return false;
            m_OutcomeListeners.Add(listener);
            return true;
        }

        public bool UnregisterOutcomeListener(ICombatOutcomeListener listener)
        {
            return listener != null && m_OutcomeListeners.Remove(listener);
        }

        public void NotifyOutcome(in ResolvedDamage damage)
        {
            for (int i = 0; i < m_OutcomeListeners.Count; i++)
            {
                m_OutcomeListeners[i].OnDamageResolved(damage);
            }
        }

        private void OnDestroy()
        {
            m_Outgoing.Clear();
            m_Incoming.Clear();
            m_OutcomeListeners.Clear();
        }
    }
}
