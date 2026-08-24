using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Unity.Items
{
    [CreateAssetMenu(
        fileName = "ApplyStatusItemEffect",
        menuName = "Vampire Hunt/Items/Use Effects/Apply Status")]
    public sealed class ApplyStatusUsableItemEffectAsset : UsableItemEffectAsset
    {
        [SerializeField] private StatusEffectDefinitionAsset status;
        [SerializeField, Min(1)] private int stacks = 1;
        [Tooltip("Zero uses the status asset's default duration.")]
        [SerializeField, Min(0f)] private float durationOverride;
        [SerializeField] private float magnitude = 1f;

        public override bool CanApply(in UsableItemEffectContext context) =>
            context.Statuses != null && status != null && status.StatusId != 0;

        public override bool TryApply(in UsableItemEffectContext context)
        {
            if (!CanApply(context)) return false;
            var definition = status.ToDomain();
            float duration = durationOverride > 0f
                ? durationOverride
                : (float)definition.DefaultDuration;
            var spec = new StatusEffectSpec(
                definition.StatusId,
                Mathf.Max(1, stacks),
                duration,
                magnitude,
                definition.Element);
            var request = new StatusApplicationRequest(
                context.EntityId,
                context.EntityId,
                spec);
            return context.Statuses.TryApplyStatus(request);
        }

        private void OnValidate()
        {
            stacks = Mathf.Max(1, stacks);
            durationOverride = Mathf.Max(0f, durationOverride);
        }
    }
}
