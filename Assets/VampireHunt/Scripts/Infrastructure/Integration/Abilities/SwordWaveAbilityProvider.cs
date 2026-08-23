using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Unity composition root for the pure sword-wave runtime.</summary>
    [DisallowMultipleComponent]
    public sealed class SwordWaveAbilityProvider : MonoBehaviour, ICombatAbilityProvider
    {
        [SerializeField] private SwordWaveAbilityAsset definition;

        public ICombatAbility CreateAbility()
        {
            if (definition != null) return definition.CreateRuntime();
            Debug.LogError("[SwordWaveAbilityProvider] Ability definition is not assigned.", this);
            return null;
        }
    }
}
