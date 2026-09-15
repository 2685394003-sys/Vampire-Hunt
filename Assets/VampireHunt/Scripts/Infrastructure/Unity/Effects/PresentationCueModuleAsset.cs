using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "PresentationCue", menuName = "Vampire Hunt/Effects/Presentation Cue")]
    public sealed class PresentationCueModuleAsset : EffectModuleAsset
    {
        [SerializeField, Min(1)] private uint cueId = 1;
        public override IEffectModuleDescriptor ToDescriptor() => new PresentationCueEffectDescriptor(cueId);
    }
}
