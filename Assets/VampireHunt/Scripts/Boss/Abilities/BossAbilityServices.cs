using System;
using VampireHunt.Contracts;

namespace VampireHunt.Boss.Abilities
{
    /// <summary>
    /// Immutable bundle injected into one ability runtime. It is not a global singleton;
    /// the Boss prefab composition root builds one bundle from its atomic adapters.
    /// </summary>
    public sealed class BossAbilityServices
    {
        public static readonly BossAbilityServices Empty = new BossAbilityServices();

        public IPlayerTargetQuery PlayerTargetQuery { get; }
        public IBossHitQuery HitQuery { get; }
        public IBossDamageService DamageService { get; }
        public IBossProjectileSpawner ProjectileSpawner { get; }
        public IBossAreaTelegraphService AreaTelegraphService { get; }
        public IBossSweepTelegraphService SweepTelegraphService { get; }
        public IBossTrackingLaserPresentationService TrackingLaserPresentationService { get; }
        public IBossFacingService FacingService { get; }
        public IBossStatusEffectService StatusEffectService { get; }
        public IRunClockModifier RunClockModifier { get; }
        public IBossBodyState BossBodyState { get; }

        public bool IsComplete =>
            PlayerTargetQuery != null &&
            HitQuery != null &&
            DamageService != null &&
            ProjectileSpawner != null &&
            StatusEffectService != null &&
            RunClockModifier != null &&
            BossBodyState != null;

        private BossAbilityServices() { }

        public BossAbilityServices(
            IPlayerTargetQuery playerTargetQuery,
            IBossHitQuery hitQuery,
            IBossDamageService damageService,
            IBossProjectileSpawner projectileSpawner,
            IBossStatusEffectService statusEffectService,
            IRunClockModifier runClockModifier,
            IBossBodyState bossBodyState,
            IBossAreaTelegraphService areaTelegraphService = null,
            IBossSweepTelegraphService sweepTelegraphService = null,
            IBossTrackingLaserPresentationService trackingLaserPresentationService = null,
            IBossFacingService facingService = null)
        {
            PlayerTargetQuery = playerTargetQuery;
            HitQuery = hitQuery;
            DamageService = damageService;
            ProjectileSpawner = projectileSpawner;
            AreaTelegraphService = areaTelegraphService;
            SweepTelegraphService = sweepTelegraphService;
            TrackingLaserPresentationService = trackingLaserPresentationService;
            FacingService = facingService;
            StatusEffectService = statusEffectService;
            RunClockModifier = runClockModifier;
            BossBodyState = bossBodyState;
        }

        public string DescribeMissingServices()
        {
            if (IsComplete) return string.Empty;

            string missing = string.Empty;
            AppendMissing(ref missing, PlayerTargetQuery, nameof(PlayerTargetQuery));
            AppendMissing(ref missing, HitQuery, nameof(HitQuery));
            AppendMissing(ref missing, DamageService, nameof(DamageService));
            AppendMissing(ref missing, ProjectileSpawner, nameof(ProjectileSpawner));
            AppendMissing(ref missing, StatusEffectService, nameof(StatusEffectService));
            AppendMissing(ref missing, RunClockModifier, nameof(RunClockModifier));
            AppendMissing(ref missing, BossBodyState, nameof(BossBodyState));
            return missing;
        }

        private static void AppendMissing<T>(ref string result, T service, string name) where T : class
        {
            if (service != null) return;
            result = string.IsNullOrEmpty(result) ? name : result + ", " + name;
        }
    }

    /// <summary>Optional capability implemented only by logic classes that use gameplay services.</summary>
    public interface IBossAbilityServiceConsumer
    {
        void BindServices(BossAbilityServices services);
    }
}
