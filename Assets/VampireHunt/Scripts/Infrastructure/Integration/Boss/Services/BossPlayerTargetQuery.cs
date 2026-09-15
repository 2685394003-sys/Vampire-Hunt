using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class BossPlayerTargetQuery : MonoBehaviour, IPlayerTargetQuery
    {
        public int QueryAlivePlayers(BossPlayerTarget[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (!BossNetworkPlayerResolver.TryGetServerManager(out NetworkManager manager)) return 0;

            int count = 0;
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count && count < results.Length; i++)
            {
                if (!BossNetworkPlayerResolver.TryCreateTarget(clients[i].PlayerObject, out var target)) continue;
                results[count++] = target;
            }

            return count;
        }

        public bool TryGetNearest(
            in Float3 origin,
            float maxDistance,
            out BossPlayerTarget target)
        {
            target = default;
            if (!BossNetworkPlayerResolver.TryGetServerManager(out NetworkManager manager)) return false;

            float limit = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;
            float bestDistance = limit;
            bool found = false;
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                if (!BossNetworkPlayerResolver.TryCreateTarget(clients[i].PlayerObject, out var candidate)) continue;
                float dx = candidate.Position.X - origin.X;
                float dy = candidate.Position.Y - origin.Y;
                float dz = candidate.Position.Z - origin.Z;
                float sqrDistance = dx * dx + dy * dy + dz * dz;
                if (sqrDistance > bestDistance) continue;
                bestDistance = sqrDistance;
                target = candidate;
                found = true;
            }

            return found;
        }

        public bool TryResolve(GameplayEntityId entityId, out BossPlayerTarget target)
        {
            target = default;
            return BossNetworkPlayerResolver.TryGetServerManager(out NetworkManager manager) &&
                   BossNetworkPlayerResolver.TryFindPlayer(manager, entityId, out NetworkObject player) &&
                   BossNetworkPlayerResolver.TryCreateTarget(player, out target);
        }
    }
}
