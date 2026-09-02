using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;
using VampireHunt.Effects;
using VampireHunt.Infrastructure.Unity.Effects;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "StatusEffect", menuName = "Vampire Hunt/Combat/Status Effect")]
    public sealed class StatusEffectDefinitionAsset : ScriptableObject
    {
        [Min(1)] [SerializeField] private uint statusId = 1;
        [SerializeField] private ElementId element;
        [SerializeField] private StatusStackPolicy stackPolicy;
        [Min(0.1f)] [SerializeField] private float maxStacks = 1f;
        [Min(0.01f)] [SerializeField] private float defaultDuration = 1f;
        [SerializeField] private EffectModuleAsset[] effectModules;

        public uint StatusId => statusId;

        public StatusEffectDefinition ToDomain()
        {
            var descriptors = new IEffectModuleDescriptor[effectModules != null ? effectModules.Length : 0];
            for (int i = 0; i < descriptors.Length; i++)
                descriptors[i] = effectModules[i] != null ? effectModules[i].ToDescriptor() : null;
            return new StatusEffectDefinition(
                statusId, element, stackPolicy, maxStacks, defaultDuration, descriptors);
        }
    }
}
