using System;
using System.Collections.Generic;
using VampireHunt.Contracts;

namespace VampireHunt.Combat
{
    /// <summary>元素反应类型（v2.2）：炸裂 = 冰打火 → AOE+击退；碎裂 = 火打冰 → 百分比伤害。</summary>
    public enum ElementReactionType : byte
    {
        None = 0,
        Detonate = 1,
        Shatter = 2
    }

    public sealed class ElementReactionDefinition
    {
        public ElementId IncomingElement { get; }
        public ElementReactionType ReactionType { get; }
        /// <summary>目标身上必须已存在的状态（炸裂=灼烧、碎裂=霜寒）。</summary>
        public uint RequiredStatusId { get; }
        public uint ConsumeFireStatusId { get; }
        public uint ConsumeFrostStatusId { get; }
        public int ConsumeFireStacks { get; }
        public int ConsumeFrostStacks { get; }
        public float DamageMultiplier { get; }
        public float ExplodeRadius { get; }
        public float KnockbackDistance { get; }
        public float PercentDamage { get; }
        public float Cooldown { get; }

        public ElementReactionDefinition(
            ElementId incomingElement,
            ElementReactionType reactionType,
            uint requiredStatusId,
            uint consumeFireStatusId,
            uint consumeFrostStatusId,
            int consumeFireStacks,
            int consumeFrostStacks,
            float damageMultiplier,
            float explodeRadius,
            float knockbackDistance,
            float percentDamage,
            float cooldown)
        {
            IncomingElement = incomingElement;
            ReactionType = reactionType;
            RequiredStatusId = requiredStatusId;
            ConsumeFireStatusId = consumeFireStatusId;
            ConsumeFrostStatusId = consumeFrostStatusId;
            ConsumeFireStacks = Math.Max(0, consumeFireStacks);
            ConsumeFrostStacks = Math.Max(0, consumeFrostStacks);
            DamageMultiplier = Math.Max(0f, damageMultiplier);
            ExplodeRadius = Math.Max(0f, explodeRadius);
            KnockbackDistance = Math.Max(0f, knockbackDistance);
            PercentDamage = Math.Max(0f, percentDamage);
            Cooldown = Math.Max(0f, cooldown);
        }
    }

    public readonly struct ElementReactionResult
    {
        public ElementReactionType Type { get; }
        public float DamageMultiplier { get; }
        public float ExplodeRadius { get; }
        public float KnockbackDistance { get; }
        public float PercentDamage { get; }
        public float Cooldown { get; }
        public uint ConsumeFireStatusId { get; }
        public uint ConsumeFrostStatusId { get; }
        public int ConsumeFireStacks { get; }
        public int ConsumeFrostStacks { get; }

        public bool HasReaction => Type != ElementReactionType.None;

        public ElementReactionResult(
            ElementReactionType type,
            float damageMultiplier,
            float explodeRadius,
            float knockbackDistance,
            float percentDamage,
            float cooldown,
            uint consumeFireStatusId,
            uint consumeFrostStatusId,
            int consumeFireStacks,
            int consumeFrostStacks)
        {
            Type = type;
            DamageMultiplier = damageMultiplier;
            ExplodeRadius = explodeRadius;
            KnockbackDistance = knockbackDistance;
            PercentDamage = percentDamage;
            Cooldown = cooldown;
            ConsumeFireStatusId = consumeFireStatusId;
            ConsumeFrostStatusId = consumeFrostStatusId;
            ConsumeFireStacks = consumeFireStacks;
            ConsumeFrostStacks = consumeFrostStacks;
        }

        public static ElementReactionResult None() =>
            new ElementReactionResult(ElementReactionType.None, 1f, 0f, 0f, 0f, 0f, 0, 0, 0, 0);
    }

    /// <summary>Catalog-backed reaction matcher（v2.2）：炸裂/碎裂 + 冷却 + 消耗层数。</summary>
    public sealed class ElementReactionResolver
    {
        private readonly List<ElementReactionDefinition> m_Definitions =
            new List<ElementReactionDefinition>();

        public ElementReactionResolver(IEnumerable<ElementReactionDefinition> definitions)
        {
            if (definitions != null) m_Definitions.AddRange(definitions);
        }

        public ElementReactionResult Resolve(ElementId incomingElement, Func<uint, bool> hasStatus)
        {
            if (incomingElement == ElementId.None || hasStatus == null)
                return ElementReactionResult.None();

            for (int i = 0; i < m_Definitions.Count; i++)
            {
                ElementReactionDefinition definition = m_Definitions[i];
                if (definition.IncomingElement != incomingElement ||
                    definition.RequiredStatusId == 0 ||
                    !hasStatus(definition.RequiredStatusId)) continue;
                return new ElementReactionResult(
                    definition.ReactionType,
                    definition.DamageMultiplier,
                    definition.ExplodeRadius,
                    definition.KnockbackDistance,
                    definition.PercentDamage,
                    definition.Cooldown,
                    definition.ConsumeFireStatusId,
                    definition.ConsumeFrostStatusId,
                    definition.ConsumeFireStacks,
                    definition.ConsumeFrostStacks);
            }
            return ElementReactionResult.None();
        }
    }
}
