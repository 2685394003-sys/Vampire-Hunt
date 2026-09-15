using System;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Combat
{
    public sealed class StatusEffectDefinition
    {
        public uint StatusId { get; }
        public ElementId Element { get; }
        public StatusStackPolicy StackPolicy { get; }
        public int MaxStacks { get; }
        public double DefaultDuration { get; }
        public EffectDefinition RuntimeEffects { get; }
        public EffectBlockFlags BlockFlags => RuntimeEffects.GetBlockFlags();
        public uint PresentationCueId => RuntimeEffects.GetPresentationCueId();

        public StatusEffectDefinition(
            uint statusId,
            ElementId element,
            StatusStackPolicy stackPolicy,
            int maxStacks,
            double defaultDuration,
            IEffectModuleDescriptor[] effectModules)
        {
            if (statusId == 0) throw new ArgumentOutOfRangeException(nameof(statusId));
            StatusId = statusId;
            Element = element;
            StackPolicy = stackPolicy;
            MaxStacks = Math.Max(1, maxStacks);
            DefaultDuration = Math.Max(0.01d, defaultDuration);
            RuntimeEffects = new EffectDefinition(
                EffectSourceKind.Status, statusId, effectModules ?? Array.Empty<IEffectModuleDescriptor>());
        }
    }
}
