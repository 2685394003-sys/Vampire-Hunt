using System;
using System.Collections.Generic;
using VampireHunt.Contracts;

namespace VampireHunt.Combat
{
    public sealed class ElementReactionDefinition
    {
        public ElementId IncomingElement { get; }
        public uint RequiredStatusId { get; }
        public bool ConsumeRequiredStatus { get; }
        public float DamageMultiplier { get; }

        public ElementReactionDefinition(
            ElementId incomingElement,
            uint requiredStatusId,
            bool consumeRequiredStatus,
            float damageMultiplier)
        {
            IncomingElement = incomingElement;
            RequiredStatusId = requiredStatusId;
            ConsumeRequiredStatus = consumeRequiredStatus;
            DamageMultiplier = Math.Max(0f, damageMultiplier);
        }
    }

    public readonly struct ElementReactionResult
    {
        public float DamageMultiplier { get; }
        public uint ConsumedStatusId { get; }

        public ElementReactionResult(float damageMultiplier, uint consumedStatusId)
        {
            DamageMultiplier = damageMultiplier;
            ConsumedStatusId = consumedStatusId;
        }
    }

    /// <summary>Catalog-backed reaction matcher with no knowledge of concrete status IDs.</summary>
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
                return new ElementReactionResult(1f, 0);

            for (int i = 0; i < m_Definitions.Count; i++)
            {
                ElementReactionDefinition definition = m_Definitions[i];
                if (definition.IncomingElement != incomingElement ||
                    definition.RequiredStatusId == 0 ||
                    !hasStatus(definition.RequiredStatusId)) continue;
                return new ElementReactionResult(
                    definition.DamageMultiplier,
                    definition.ConsumeRequiredStatus ? definition.RequiredStatusId : 0);
            }
            return new ElementReactionResult(1f, 0);
        }
    }
}
