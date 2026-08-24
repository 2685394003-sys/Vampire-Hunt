using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Thin composition root. It discovers sibling atomic adapters once and builds
    /// the immutable bundle injected into ability logic; it performs no gameplay work.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class BossAbilityServiceHost : MonoBehaviour
    {
        [SerializeField] private bool logMissingServices = true;

        public BossAbilityServices Services { get; private set; } = BossAbilityServices.Empty;

        private void Awake()
        {
            Rebuild(logMissingServices);
        }

        public bool TryBuild(out BossAbilityServices services)
        {
            Rebuild(logMissingServices);
            services = Services;
            return services.IsComplete;
        }

        private void Rebuild(bool logMissing)
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            Services = new BossAbilityServices(
                Find<IPlayerTargetQuery>(behaviours),
                Find<IBossHitQuery>(behaviours),
                Find<IBossDamageService>(behaviours),
                Find<IBossProjectileSpawner>(behaviours),
                Find<IBossStatusEffectService>(behaviours),
                Find<IRunClockModifier>(behaviours),
                Find<IBossBodyState>(behaviours));

            if (logMissing && !Services.IsComplete)
            {
                Debug.LogError(
                    $"[BossAbilityServiceHost] Missing sibling adapters: {Services.DescribeMissingServices()}.",
                    this);
            }
        }

        private static T Find<T>(MonoBehaviour[] behaviours) where T : class
        {
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is T service) return service;
            return null;
        }
    }
}
