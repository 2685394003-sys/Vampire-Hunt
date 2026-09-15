using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Unity composition root for the continuous laser weapon.
    /// Multiple instances are allowed so several beam-type weapons can coexist.
    /// </summary>
    public sealed class LaserWeaponAbilityProvider : MonoBehaviour, ICombatAbilityProvider
    {
        [SerializeField] private LaserWeaponAbilityAsset definition;

        public ICombatAbility CreateAbility()
        {
            if (definition != null) return definition.CreateRuntime();
            Debug.LogError("[LaserWeaponAbilityProvider] Ability definition is not assigned.", this);
            return null;
        }
    }
}
