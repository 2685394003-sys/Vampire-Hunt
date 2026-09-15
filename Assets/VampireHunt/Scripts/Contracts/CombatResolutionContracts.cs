using System;
using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    [Flags]
    public enum CombatParticipantRole : byte
    {
        None = 0,
        Source = 1 << 0,
        Target = 1 << 1
    }

    /// <summary>
    /// Immutable server result produced after health has actually changed.
    /// Gameplay triggers must consume this record instead of presentation events.
    /// </summary>
    public readonly struct CombatResolutionRecord
    {
        public ResolvedDamage Damage { get; }
        public float TargetHealthBefore { get; }
        public float TargetHealthAfter { get; }
        public float AppliedDamage { get; }
        public bool WasKilled { get; }

        public EntityId Source => Damage.Request.Source;
        public EntityId Target => Damage.Request.Target;
        public uint AttackId => Damage.Request.AttackId;
        public ulong Sequence => Damage.Request.Sequence;
        public DamageTags Tags => Damage.Tags;

        public CombatResolutionRecord(
            in ResolvedDamage damage,
            float targetHealthBefore,
            float targetHealthAfter)
        {
            Damage = damage;
            TargetHealthBefore = Math.Max(0f, targetHealthBefore);
            TargetHealthAfter = Math.Max(0f, targetHealthAfter);
            AppliedDamage = Math.Max(0f, TargetHealthBefore - TargetHealthAfter);
            WasKilled = TargetHealthBefore > 0f && TargetHealthAfter <= 0f && AppliedDamage > 0f;
        }
    }

    /// <summary>Server-only gameplay callback used by blood-pact runtime effects.</summary>
    public interface IServerCombatResolutionListener
    {
        int Priority { get; }
        void OnCombatResolved(in CombatResolutionRecord record, CombatParticipantRole role);
    }

    public interface IServerCombatResolutionTarget
    {
        bool RegisterCombatResolutionListener(IServerCombatResolutionListener listener);
        bool UnregisterCombatResolutionListener(IServerCombatResolutionListener listener);
    }
}
