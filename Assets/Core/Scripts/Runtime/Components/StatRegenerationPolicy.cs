using System;

namespace Blocks.Gameplay.Core
{
    public enum StatUseKind : byte { Standard, Continuous, Burst }

    /// <summary>Optional gameplay policy; the template remains the sole stat writer.</summary>
    public interface IStatRegenerationPolicy
    {
        float GetRate(int statId, float maximum, float configuredRate);
        float GetDelay(int statId, StatUseKind useKind, float configuredDelay);
    }

    public readonly struct AuthorityStatChange
    {
        public readonly int StatId;
        public readonly float Before, After;
        public readonly bool CapacityChanged;
        public AuthorityStatChange(int id, float before, float after, bool capacityChanged)
        { StatId = id; Before = before; After = after; CapacityChanged = capacityChanged; }
    }

    public static class RegenerationMath
    {
        public static float Amount(double from, double to, double readyAt, float rate)
        {
            if (rate <= 0 || float.IsNaN(rate) || float.IsInfinity(rate)) return 0;
            return (float)Math.Max(0, to - Math.Max(from, readyAt)) * rate;
        }
    }
}
