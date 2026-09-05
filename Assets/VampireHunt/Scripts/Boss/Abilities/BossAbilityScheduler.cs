using System;
using System.Collections.Generic;

namespace VampireHunt.Boss.Abilities
{
    internal sealed class BossAbilityScheduleSlot
    {
        public BossPhaseAbilityEntry Entry;
        public double NextReadyTime;
        public int Uses;
    }

    public sealed class BossAbilityScheduler
    {
        private readonly List<BossAbilityScheduleSlot> m_Slots = new List<BossAbilityScheduleSlot>();

        internal void Load(BossPhaseDefinition phase, double serverTime)
        {
            m_Slots.Clear();
            if (phase == null) return;

            var seenIds = new HashSet<uint>();
            for (int i = 0; i < phase.Abilities.Length; i++)
            {
                BossPhaseAbilityEntry entry = phase.Abilities[i];
                if (entry?.Ability == null || entry.WeightMultiplier <= 0f ||
                    !seenIds.Add(entry.Ability.AbilityId)) continue;
                m_Slots.Add(new BossAbilityScheduleSlot
                {
                    Entry = entry,
                    NextReadyTime = serverTime + entry.InitialCooldown,
                    Uses = 0
                });
            }
        }

        internal bool TrySelect(
            double serverTime,
            in BossAbilitySelectionContext context,
            float random01,
            out BossAbilityScheduleSlot selected)
        {
            selected = null;
            float totalWeight = 0f;

            for (int i = 0; i < m_Slots.Count; i++)
            {
                BossAbilityScheduleSlot slot = m_Slots[i];
                if (!CanUse(slot, serverTime, context)) continue;
                totalWeight += slot.Entry.Ability.BaseWeight * slot.Entry.WeightMultiplier;
            }

            if (totalWeight <= 0f) return false;
            float cursor = Math.Max(0f, Math.Min(0.999999f, random01)) * totalWeight;

            for (int i = 0; i < m_Slots.Count; i++)
            {
                BossAbilityScheduleSlot slot = m_Slots[i];
                if (!CanUse(slot, serverTime, context)) continue;
                cursor -= slot.Entry.Ability.BaseWeight * slot.Entry.WeightMultiplier;
                if (cursor > 0f) continue;
                selected = slot;
                return true;
            }

            return false;
        }

        internal static void CommitCast(
            BossAbilityScheduleSlot slot,
            double serverTime,
            double castDuration,
            double cooldown)
        {
            if (slot?.Entry?.Ability == null) return;
            slot.Uses++;
            slot.NextReadyTime = serverTime + Math.Max(0d, castDuration) + Math.Max(0d, cooldown);
        }

        private static bool CanUse(
            BossAbilityScheduleSlot slot,
            double serverTime,
            in BossAbilitySelectionContext context)
        {
            if (slot?.Entry?.Ability == null || serverTime < slot.NextReadyTime) return false;
            BossPhaseAbilityEntry entry = slot.Entry;
            if (entry.Ability.OneShot && slot.Uses > 0) return false;
            if (entry.MaxUses > 0 && slot.Uses >= entry.MaxUses) return false;
            return entry.Ability.CanUse(context);
        }
    }
}
