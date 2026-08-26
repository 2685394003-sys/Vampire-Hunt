using System;
using VampireHunt.Contracts;

namespace VampireHunt.Boss.Abilities
{
    public enum BossAbilityCastPhase : byte
    {
        None = 0,
        Telegraph = 1,
        Resolve = 2,
        Recover = 3
    }

    public readonly struct BossAbilitySelectionContext
    {
        public float TargetDistance { get; }
        public float NormalizedHealth { get; }
        public bool HasTarget { get; }

        public BossAbilitySelectionContext(float targetDistance, float normalizedHealth, bool hasTarget)
        {
            TargetDistance = Math.Max(0f, targetDistance);
            NormalizedHealth = Math.Max(0f, Math.Min(1f, normalizedHealth));
            HasTarget = hasTarget;
        }
    }

    public readonly struct BossAbilityCastContext
    {
        public uint AbilityId { get; }
        public ulong CastSequence { get; }
        public double StartServerTime { get; }
        public ulong TargetEntityId { get; }
        public Float3 SourcePosition { get; }
        public Float3 TargetPosition { get; }
        public Float3 Direction { get; }
        public uint RandomSeed { get; }

        public BossAbilityCastContext(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            ulong targetEntityId,
            in Float3 sourcePosition,
            in Float3 targetPosition,
            in Float3 direction,
            uint randomSeed)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            StartServerTime = startServerTime;
            TargetEntityId = targetEntityId;
            SourcePosition = sourcePosition;
            TargetPosition = targetPosition;
            Direction = direction;
            RandomSeed = randomSeed;
        }
    }

    /// <summary>
    /// One immutable server-side sample of the information an ability scheduler needs.
    /// Unity target discovery and stat lookup are performed by separate adapters before
    /// this value reaches the pure ability runtime.
    /// </summary>
    public readonly struct BossAbilityExecutionInput
    {
        public BossAbilitySelectionContext Selection { get; }
        public ulong TargetEntityId { get; }
        public Float3 SourcePosition { get; }
        public Float3 TargetPosition { get; }
        public Float3 Direction { get; }

        public BossAbilityExecutionInput(
            in BossAbilitySelectionContext selection,
            ulong targetEntityId,
            in Float3 sourcePosition,
            in Float3 targetPosition,
            in Float3 direction)
        {
            Selection = selection;
            TargetEntityId = targetEntityId;
            SourcePosition = sourcePosition;
            TargetPosition = targetPosition;
            Direction = direction;
        }
    }

    public interface IBossAbilityLogicRuntime : IDisposable
    {
        void OnCastStarted(in BossAbilityCastContext context);
        void OnPhaseEntered(BossAbilityCastPhase phase, double serverTime);
        void Tick(double serverTime);
        void Cancel(double serverTime);
    }

    public sealed class BossAbilityDefinition
    {
        private readonly Func<IBossAbilityLogicRuntime> m_CreateLogic;

        public uint AbilityId { get; }
        public string DisplayName { get; }
        public float BaseWeight { get; }
        public double Cooldown { get; }
        public float MinDistance { get; }
        public float MaxDistance { get; }
        public float MinNormalizedHealth { get; }
        public float MaxNormalizedHealth { get; }
        public bool RequiresTarget { get; }
        public bool OneShot { get; }
        public bool ParryableDuringTelegraph { get; }
        public double TelegraphDuration { get; }
        public double ResolveDuration { get; }
        public double RecoverDuration { get; }
        public double TotalDuration => TelegraphDuration + ResolveDuration + RecoverDuration;

        public BossAbilityDefinition(
            uint abilityId,
            string displayName,
            float baseWeight,
            double cooldown,
            float minDistance,
            float maxDistance,
            float minNormalizedHealth,
            float maxNormalizedHealth,
            bool requiresTarget,
            bool oneShot,
            double telegraphDuration,
            double resolveDuration,
            double recoverDuration,
            Func<IBossAbilityLogicRuntime> createLogic,
            bool parryableDuringTelegraph = false)
        {
            if (abilityId == 0) throw new ArgumentOutOfRangeException(nameof(abilityId));
            AbilityId = abilityId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Boss Ability {abilityId}" : displayName;
            BaseWeight = Math.Max(0.0001f, baseWeight);
            Cooldown = Math.Max(0d, cooldown);
            MinDistance = Math.Max(0f, minDistance);
            MaxDistance = Math.Max(MinDistance, maxDistance);
            MinNormalizedHealth = Math.Max(0f, Math.Min(1f, minNormalizedHealth));
            MaxNormalizedHealth = Math.Max(MinNormalizedHealth, Math.Min(1f, maxNormalizedHealth));
            RequiresTarget = requiresTarget;
            OneShot = oneShot;
            ParryableDuringTelegraph = parryableDuringTelegraph;
            TelegraphDuration = Math.Max(0d, telegraphDuration);
            ResolveDuration = Math.Max(0.01d, resolveDuration);
            RecoverDuration = Math.Max(0d, recoverDuration);
            m_CreateLogic = createLogic ?? throw new ArgumentNullException(nameof(createLogic));
        }

        public IBossAbilityLogicRuntime CreateLogic() =>
            m_CreateLogic() ?? throw new InvalidOperationException(
                $"Boss ability {AbilityId} logic factory returned null.");

        public bool CanUse(in BossAbilitySelectionContext context)
        {
            if (RequiresTarget && !context.HasTarget) return false;
            return context.TargetDistance >= MinDistance &&
                   context.TargetDistance <= MaxDistance &&
                   context.NormalizedHealth >= MinNormalizedHealth &&
                   context.NormalizedHealth <= MaxNormalizedHealth;
        }
    }

    public sealed class BossPhaseAbilityEntry
    {
        public BossAbilityDefinition Ability { get; }
        public float WeightMultiplier { get; }
        public int MaxUses { get; }
        public double InitialCooldown { get; }

        public BossPhaseAbilityEntry(
            BossAbilityDefinition ability,
            float weightMultiplier,
            int maxUses,
            double initialCooldown)
        {
            Ability = ability ?? throw new ArgumentNullException(nameof(ability));
            WeightMultiplier = Math.Max(0f, weightMultiplier);
            MaxUses = Math.Max(0, maxUses);
            InitialCooldown = Math.Max(0d, initialCooldown);
        }
    }

    public sealed class BossPhaseDefinition
    {
        public int PhaseNumber { get; }
        public string DisplayName { get; }
        public BossPhaseAbilityEntry[] Abilities { get; }

        public BossPhaseDefinition(int phaseNumber, string displayName, BossPhaseAbilityEntry[] abilities)
        {
            PhaseNumber = Math.Max(1, phaseNumber);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"Phase {PhaseNumber}" : displayName;
            Abilities = abilities ?? Array.Empty<BossPhaseAbilityEntry>();
        }
    }

    public readonly struct BossAbilitySnapshot
    {
        public int PhaseNumber { get; }
        public uint AbilityId { get; }
        public ulong CastSequence { get; }
        public BossAbilityCastPhase CastPhase { get; }
        public double CastStartServerTime { get; }
        public double PhaseStartServerTime { get; }
        public double CastEndServerTime { get; }
        public ulong TargetEntityId { get; }
        public Float3 TargetPosition { get; }
        public Float3 Direction { get; }
        public uint RandomSeed { get; }
        public uint Revision { get; }

        public bool IsCasting => AbilityId != 0 && CastPhase != BossAbilityCastPhase.None;

        public BossAbilitySnapshot(
            int phaseNumber,
            uint abilityId,
            ulong castSequence,
            BossAbilityCastPhase castPhase,
            double castStartServerTime,
            double phaseStartServerTime,
            double castEndServerTime,
            ulong targetEntityId,
            in Float3 targetPosition,
            in Float3 direction,
            uint randomSeed,
            uint revision)
        {
            PhaseNumber = phaseNumber;
            AbilityId = abilityId;
            CastSequence = castSequence;
            CastPhase = castPhase;
            CastStartServerTime = castStartServerTime;
            PhaseStartServerTime = phaseStartServerTime;
            CastEndServerTime = castEndServerTime;
            TargetEntityId = targetEntityId;
            TargetPosition = targetPosition;
            Direction = direction;
            RandomSeed = randomSeed;
            Revision = revision;
        }
    }
}
