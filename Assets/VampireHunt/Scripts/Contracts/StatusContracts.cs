using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    public enum ElementId : byte
    {
        None = 0,
        Fire = 1,
        Ice = 2
    }

    public enum StatusStackPolicy : byte
    {
        RefreshDuration = 0,
        AddStacksAndRefresh = 1,
        ReplaceIfStronger = 2
    }

    public static class StatusEffectIds
    {
        public const uint Burn = 1;
        public const uint Frost = 2;
        public const uint Frozen = 3;
    }

    public readonly struct StatusEffectSpec
    {
        public uint StatusId { get; }
        public int Stacks { get; }
        public float Duration { get; }
        public float Magnitude { get; }
        public ElementId Element { get; }

        public StatusEffectSpec(uint statusId, int stacks, float duration, float magnitude, ElementId element)
        {
            StatusId = statusId;
            Stacks = stacks > 0 ? stacks : 1;
            Duration = duration;
            Magnitude = magnitude;
            Element = element;
        }
    }

    public readonly struct StatusApplicationRequest
    {
        public EntityId Source { get; }
        public EntityId Target { get; }
        public StatusEffectSpec Spec { get; }

        public StatusApplicationRequest(EntityId source, EntityId target, in StatusEffectSpec spec)
        {
            Source = source;
            Target = target;
            Spec = spec;
        }
    }

    public interface IStatusEffectTarget
    {
        bool TryApplyStatus(in StatusApplicationRequest request);
        bool HasStatus(uint statusId);
        bool RemoveStatus(uint statusId);
    }

    public interface ICombatEntityIdentity
    {
        EntityId CombatEntityId { get; }
    }
}
