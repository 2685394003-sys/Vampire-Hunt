using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Shared implementation detail for the small Boss-to-player adapters.</summary>
    internal static class BossNetworkPlayerResolver
    {
        public static bool TryGetServerManager(out NetworkManager manager)
        {
            manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        public static bool TryFindPlayer(
            NetworkManager manager,
            GameplayEntityId entityId,
            out NetworkObject player)
        {
            player = null;
            if (manager == null || entityId.IsNone) return false;

            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                NetworkObject candidate = clients[i].PlayerObject;
                if (candidate == null || ResolveEntityId(candidate) != entityId) continue;
                player = candidate;
                return true;
            }

            return false;
        }

        public static bool TryCreateTarget(NetworkObject player, out BossPlayerTarget target)
        {
            target = default;
            if (player == null || !player.gameObject.activeInHierarchy) return false;

            CoreStatsHandler stats = player.GetComponent<CoreStatsHandler>();
            if (stats != null && !stats.IsAlive) return false;

            Vector3 position = player.transform.position;
            float normalSpeed = player.TryGetComponent(out CoreMovement movement) ? movement.moveSpeed : 0f;
            target = new BossPlayerTarget(
                ResolveEntityId(player),
                new Float3(position.x, position.y, position.z),
                normalSpeed);
            return !target.EntityId.IsNone;
        }

        public static GameplayEntityId ResolveEntityId(NetworkObject player)
        {
            if (player == null) return GameplayEntityId.None;
            MonoBehaviour[] behaviours = player.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICombatEntityIdentity identity)
                    return identity.CombatEntityId;
            }

            return player.IsPlayerObject
                ? new GameplayEntityId(player.OwnerClientId + 1UL)
                : GameplayEntityId.None;
        }

        public static bool TryGetPort<T>(NetworkObject player, out T port) where T : class
        {
            port = null;
            if (player == null) return false;
            MonoBehaviour[] behaviours = player.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is T candidate)) continue;
                port = candidate;
                return true;
            }

            return false;
        }
    }
}
