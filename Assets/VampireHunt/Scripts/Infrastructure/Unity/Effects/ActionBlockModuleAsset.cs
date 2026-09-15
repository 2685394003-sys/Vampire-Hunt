using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "ActionBlock", menuName = "Vampire Hunt/Effects/Action Block")]
    public sealed class ActionBlockModuleAsset : EffectModuleAsset
    {
        [SerializeField] private EffectBlockFlags flags = EffectBlockFlags.Action | EffectBlockFlags.Movement;
        public override IEffectModuleDescriptor ToDescriptor() => new ActionBlockEffectDescriptor(flags);
    }
}
