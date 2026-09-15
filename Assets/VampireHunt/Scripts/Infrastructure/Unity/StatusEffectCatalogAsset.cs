using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "StatusEffectCatalog", menuName = "Vampire Hunt/Combat/Status Effect Catalog")]
    public sealed class StatusEffectCatalogAsset : ScriptableObject
    {
        [Serializable]
        private struct ReactionEntry
        {
            public ElementId incomingElement;
            public uint requiredStatusId;
            public bool consumeRequiredStatus;
            [Min(0f)] public float damageMultiplier;

            public ElementReactionDefinition ToDomain() => new ElementReactionDefinition(
                incomingElement, requiredStatusId, consumeRequiredStatus, damageMultiplier);
        }

        [SerializeField] private StatusEffectDefinitionAsset[] definitions;
        [SerializeField] private ReactionEntry[] reactions;

        private ElementReactionResolver m_ReactionResolver;

        public bool TryGet(uint statusId, out StatusEffectDefinition definition)
        {
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    StatusEffectDefinitionAsset asset = definitions[i];
                    if (asset == null || asset.StatusId != statusId) continue;
                    definition = asset.ToDomain();
                    return true;
                }
            }

            definition = null;
            return false;
        }

        public ElementReactionResolver CreateReactionResolver()
        {
            if (m_ReactionResolver != null) return m_ReactionResolver;
            var domain = new List<ElementReactionDefinition>();
            if (reactions != null)
                for (int i = 0; i < reactions.Length; i++) domain.Add(reactions[i].ToDomain());
            m_ReactionResolver = new ElementReactionResolver(domain);
            return m_ReactionResolver;
        }

        private void OnValidate() => m_ReactionResolver = null;
    }
}
