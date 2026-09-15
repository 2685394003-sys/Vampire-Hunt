using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode.Player
{
    /// <summary>One scheduler per NetworkManager, no static state or object scans.</summary>
    public sealed class PlayerRecoveryScheduler : MonoBehaviour
    {
        private readonly List<PlayerCombatStateHost> m_Players = new List<PlayerCombatStateHost>();
        private double m_NextTick;
        public static PlayerRecoveryScheduler GetOrCreate(NetworkManager manager)
        {
            if (!manager.TryGetComponent(out PlayerRecoveryScheduler scheduler))
                scheduler = manager.gameObject.AddComponent<PlayerRecoveryScheduler>();
            return scheduler;
        }
        public void Register(PlayerCombatStateHost player)
        { if (!m_Players.Contains(player)) m_Players.Add(player); }
        public void Unregister(PlayerCombatStateHost player) { m_Players.Remove(player); }
        private void LateUpdate()
        {
            double now = Time.timeAsDouble;
            if (Time.timeScale <= 0 || now < m_NextTick) return;
            m_NextTick = now + 0.05;
            for (int i = m_Players.Count - 1; i >= 0; i--)
            {
                if (i >= m_Players.Count) continue;
                var player = m_Players[i];
                if (player == null) { m_Players.RemoveAt(i); continue; }
                player.AdvanceServer(now);
            }
        }
    }
}
