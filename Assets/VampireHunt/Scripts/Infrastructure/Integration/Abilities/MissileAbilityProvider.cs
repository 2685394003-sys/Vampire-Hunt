using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Unity composition root for the missile bomb weapon. Multiple instances are allowed.
    /// </summary>
    public sealed class MissileAbilityProvider : MonoBehaviour, ICombatAbilityProvider
    {
        [SerializeField] private MissileAbilityAsset definition;

        public ICombatAbility CreateAbility()
        {
            if (definition != null) return definition.CreateRuntime();
            Debug.LogError("[MissileAbilityProvider] Ability definition is not assigned.", this);
            return null;
        }
    }
}
