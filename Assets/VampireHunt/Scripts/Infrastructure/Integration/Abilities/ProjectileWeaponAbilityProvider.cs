using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Unity composition root for a projectile weapon ability (sniper, rifle, ...).
    /// One instance per weapon; multiple instances are allowed so several projectile weapons
    /// can coexist on the same player (each with its own definition).
    /// </summary>
    public sealed class ProjectileWeaponAbilityProvider : MonoBehaviour, ICombatAbilityProvider
    {
        [SerializeField] private ProjectileWeaponAbilityAsset definition;

        public ICombatAbility CreateAbility()
        {
            if (definition != null) return definition.CreateRuntime();
            Debug.LogError("[ProjectileWeaponAbilityProvider] Ability definition is not assigned.", this);
            return null;
        }
    }
}
