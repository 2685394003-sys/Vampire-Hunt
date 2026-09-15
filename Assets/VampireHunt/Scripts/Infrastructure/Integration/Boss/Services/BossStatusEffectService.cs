using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class BossStatusEffectService : MonoBehaviour, IBossStatusEffectService
    {
        public bool TryApply(in StatusApplicationRequest request)
        {
            return TryGetTarget(request.Target, out IStatusEffectTarget target) &&
                   target.TryApplyStatus(request);
        }

        public bool HasStatus(GameplayEntityId targetEntityId, uint statusId)
        {
            return TryGetTarget(targetEntityId, out IStatusEffectTarget target) &&
                   target.HasStatus(statusId);
        }

        public bool TryRemove(GameplayEntityId targetEntityId, uint statusId)
        {
            return TryGetTarget(targetEntityId, out IStatusEffectTarget target) &&
                   target.RemoveStatus(statusId);
        }

        private static bool TryGetTarget(GameplayEntityId entityId, out IStatusEffectTarget target)
        {
            target = null;
            return BossNetworkPlayerResolver.TryGetServerManager(out NetworkManager manager) &&
                   BossNetworkPlayerResolver.TryFindPlayer(manager, entityId, out NetworkObject player) &&
                   BossNetworkPlayerResolver.TryGetPort(player, out target);
        }
    }
}
