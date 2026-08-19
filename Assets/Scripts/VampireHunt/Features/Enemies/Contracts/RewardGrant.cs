using System;
using System.Collections.Generic;

namespace VampireHunt.Enemies.Contracts
{
    public readonly struct RewardItem
    {
        public RewardItem(ushort kind, int amount)
        {
            Kind = kind;
            Amount = amount < 0 ? 0 : amount;
        }

        public ushort Kind { get; }
        public int Amount { get; }
    }

    /// <summary>Immutable reward payload; the reward adapter owns currency semantics.</summary>
    public sealed class RewardGrant
    {
        private readonly RewardItem[] _items;
        private readonly IReadOnlyList<RewardItem> _itemsView;

        public RewardGrant(int scarlet)
            : this(scarlet, 0)
        {
        }

        public RewardGrant(int scarlet, int experience, IReadOnlyList<RewardItem> items = null)
            : this(scarlet, 0, experience, items)
        {
        }

        public RewardGrant(int scarlet, int coins, int experience, IReadOnlyList<RewardItem> items = null)
        {
            Scarlet = Math.Max(0, scarlet);
            Coins = Math.Max(0, coins);
            Experience = Math.Max(0, experience);
            _items = items == null ? Array.Empty<RewardItem>() : Copy(items);
            _itemsView = Array.AsReadOnly(_items);
        }

        public int Scarlet { get; }
        public int Coins { get; }
        public int Experience { get; }
        public IReadOnlyList<RewardItem> Items => _itemsView;
        public bool IsEmpty => Scarlet == 0 && Coins == 0 && Experience == 0 && _items.Length == 0;

        private static RewardItem[] Copy(IReadOnlyList<RewardItem> source)
        {
            RewardItem[] copy = new RewardItem[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }
}
