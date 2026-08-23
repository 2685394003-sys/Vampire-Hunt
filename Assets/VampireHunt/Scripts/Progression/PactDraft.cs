using System;

namespace VampireHunt.Progression
{
    public sealed class PactDraft
    {
        public ulong OfferId { get; }
        public uint[] Options { get; }
        public int RerollCount { get; }
        public int RerollCost { get; }
        public bool IsActive => OfferId != 0 && Options.Length > 0;

        public PactDraft(ulong offerId, uint[] options, int rerollCount, int rerollCost)
        {
            OfferId = offerId;
            Options = options ?? Array.Empty<uint>();
            RerollCount = Math.Max(0, rerollCount);
            RerollCost = Math.Max(0, rerollCost);
        }

        public bool Contains(uint pactId)
        {
            for (int i = 0; i < Options.Length; i++) if (Options[i] == pactId) return true;
            return false;
        }
    }
}
