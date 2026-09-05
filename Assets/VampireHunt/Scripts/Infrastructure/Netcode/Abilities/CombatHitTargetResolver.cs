using System.Collections.Generic;
using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Reusable Unity adapter that resolves a collider to the preferred combat receive port without
    /// allocating a component array for every overlap hit.
    /// </summary>
    internal sealed class CombatHitTargetResolver
    {
        private readonly List<MonoBehaviour> m_ComponentBuffer = new List<MonoBehaviour>(8);

        public bool TryResolve(
            Collider collider,
            out MonoBehaviour target,
            out ITrustedCombatHitTarget trustedTarget,
            out IHittable fallback)
        {
            m_ComponentBuffer.Clear();
            collider.GetComponentsInParent(false, m_ComponentBuffer);
            for (int i = 0; i < m_ComponentBuffer.Count; i++)
            {
                if (m_ComponentBuffer[i] is not ITrustedCombatHitTarget trusted) continue;
                target = m_ComponentBuffer[i];
                trustedTarget = trusted;
                fallback = null;
                return true;
            }

            for (int i = 0; i < m_ComponentBuffer.Count; i++)
            {
                if (m_ComponentBuffer[i] is not IHittable hittable) continue;
                target = m_ComponentBuffer[i];
                trustedTarget = null;
                fallback = hittable;
                return true;
            }

            target = null;
            trustedTarget = null;
            fallback = null;
            return false;
        }
    }
}
