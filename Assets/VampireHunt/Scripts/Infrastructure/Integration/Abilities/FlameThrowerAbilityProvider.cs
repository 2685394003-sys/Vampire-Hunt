using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Unity composition root for the cone flamethrower. Multiple instances are allowed.
    /// </summary>
    public sealed class FlameThrowerAbilityProvider : MonoBehaviour, ICombatAbilityProvider
    {
        [SerializeField] private FlameThrowerAbilityAsset definition;

        public ICombatAbility CreateAbility()
        {
            if (definition != null) return definition.CreateRuntime();
            Debug.LogError("[FlameThrowerAbilityProvider] Ability definition is not assigned.", this);
            return null;
        }
    }
}
