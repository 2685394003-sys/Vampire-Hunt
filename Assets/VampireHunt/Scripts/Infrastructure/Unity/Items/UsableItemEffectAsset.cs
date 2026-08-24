using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Unity.Items
{
    public readonly struct UsableItemEffectContext
    {
        public GameObject User { get; }
        public ulong OwnerClientId { get; }
        public GameplayEntityId EntityId { get; }
        public CoreStatsHandler Stats { get; }
        public CombatStatusHost Statuses { get; }

        public UsableItemEffectContext(
            GameObject user,
            ulong ownerClientId,
            GameplayEntityId entityId,
            CoreStatsHandler stats,
            CombatStatusHost statuses)
        {
            User = user;
            OwnerClientId = ownerClientId;
            EntityId = entityId;
            Stats = stats;
            Statuses = statuses;
        }
    }

    /// <summary>
    /// Asset-configured server effect used by a usable-item definition. Implementations must
    /// keep CanApply side-effect free and return true from TryApply only when gameplay changed.
    /// </summary>
    public abstract class UsableItemEffectAsset : ScriptableObject
    {
        public abstract bool CanApply(in UsableItemEffectContext context);
        public abstract bool TryApply(in UsableItemEffectContext context);
    }
}
