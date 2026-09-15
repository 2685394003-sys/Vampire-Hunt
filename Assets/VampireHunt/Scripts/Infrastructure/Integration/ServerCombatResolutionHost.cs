using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Per-player server gameplay event host. Modular effect nodes register triggered effects here.
    /// This is intentionally separate from ScriptableObject presentation event channels.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ServerCombatResolutionHost : NetworkBehaviour, IServerCombatResolutionTarget
    {
        private readonly List<IServerCombatResolutionListener> m_Listeners =
            new List<IServerCombatResolutionListener>();

        public bool RegisterCombatResolutionListener(IServerCombatResolutionListener listener)
        {
            if (listener == null || m_Listeners.Contains(listener)) return false;

            int insertIndex = m_Listeners.Count;
            for (int i = 0; i < m_Listeners.Count; i++)
            {
                if (listener.Priority >= m_Listeners[i].Priority) continue;
                insertIndex = i;
                break;
            }
            m_Listeners.Insert(insertIndex, listener);
            return true;
        }

        public bool UnregisterCombatResolutionListener(IServerCombatResolutionListener listener)
        {
            return listener != null && m_Listeners.Remove(listener);
        }

        internal void PublishServer(in CombatResolutionRecord record, CombatParticipantRole role)
        {
            if (!IsSpawned || !IsServer || role == CombatParticipantRole.None) return;

            int listenerCount = m_Listeners.Count;
            for (int i = 0; i < listenerCount && i < m_Listeners.Count; i++)
            {
                try
                {
                    m_Listeners[i].OnCombatResolved(record, role);
                }
                catch (Exception exception)
                {
                    // One pact must not interrupt death, reward or other pact processing.
                    Debug.LogException(exception, this);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Listeners.Clear();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            m_Listeners.Clear();
            base.OnDestroy();
        }
    }

    /// <summary>Stateless server router from a combat result to involved player-owned hosts.</summary>
    public static class ServerCombatResolutionRouter
    {
        public static void Publish(NetworkManager manager, in CombatResolutionRecord record)
        {
            if (manager == null || !manager.IsServer || record.AppliedDamage <= 0f) return;

            ServerCombatResolutionHost sourceHost = TryResolvePlayerHost(manager, record.Source);
            ServerCombatResolutionHost targetHost = TryResolvePlayerHost(manager, record.Target);

            if (sourceHost != null && sourceHost == targetHost)
            {
                sourceHost.PublishServer(record, CombatParticipantRole.Source | CombatParticipantRole.Target);
                return;
            }

            sourceHost?.PublishServer(record, CombatParticipantRole.Source);
            targetHost?.PublishServer(record, CombatParticipantRole.Target);
        }

        private static ServerCombatResolutionHost TryResolvePlayerHost(NetworkManager manager, GameplayEntityId entityId)
        {
            if (entityId.IsNone) return null;
            ulong clientId = entityId.Value - 1UL;
            if (!manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
                client.PlayerObject == null)
            {
                return null;
            }
            return client.PlayerObject.GetComponent<ServerCombatResolutionHost>();
        }
    }
}
