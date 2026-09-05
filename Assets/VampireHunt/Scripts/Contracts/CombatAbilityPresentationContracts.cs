using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    /// <summary>Transient ability presentation emitted after the server accepts and executes a cast.</summary>
    public readonly struct CombatAbilityPresentationCue
    {
        public uint AbilityId { get; }
        public Float3 Origin { get; }
        public Float3 Direction { get; }
        public float Range { get; }

        public CombatAbilityPresentationCue(uint abilityId, in Float3 origin, in Float3 direction, float range)
        {
            AbilityId = abilityId;
            Origin = origin;
            Direction = direction;
            Range = range;
        }
    }

    /// <summary>Client-side sink. It may play VFX/audio but cannot mutate gameplay state.</summary>
    public interface ICombatAbilityPresentationSink
    {
        bool HandlesAbility(uint abilityId);
        void PresentAbility(in CombatAbilityPresentationCue cue);
    }
}
