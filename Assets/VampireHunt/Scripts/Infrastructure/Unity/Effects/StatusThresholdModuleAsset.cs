using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    [CreateAssetMenu(fileName = "StatusThreshold", menuName = "Vampire Hunt/Effects/Status Threshold")]
    public sealed class StatusThresholdModuleAsset : EffectModuleAsset
    {
        [SerializeField, Min(1)] private int threshold = 1;
        [SerializeField, Min(1)] private uint triggeredStatusId = 1;
        [SerializeField, Min(1)] private int triggeredStacks = 1;
        [SerializeField] private bool consumeSource = true;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new StatusThresholdEffectDescriptor(
                threshold, triggeredStatusId, triggeredStacks, consumeSource);
    }
}
