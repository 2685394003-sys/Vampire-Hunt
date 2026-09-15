using System.Collections.Generic;
using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
{
    /// <summary>
    /// 两类使魔共用的 Unity 物理索敌适配器。它只负责查询、缓存、排序和目标端口解析，
    /// 不推进使魔状态机，也不计算或提交伤害。
    /// </summary>
    internal sealed class FamiliarTargetQuery
    {
        private readonly Dictionary<GameplayEntityId, MonoBehaviour> m_EnemyCache =
            new Dictionary<GameplayEntityId, MonoBehaviour>();
        private readonly List<Collider> m_CommandTargets = new List<Collider>();
        private readonly System.Comparison<Collider> m_DistanceComparison;

        private Collider[] m_ScanBuffer = new Collider[64];
        private Collider[] m_CommandBuffer = new Collider[16];
        private Vector3 m_SortOrigin;

        public FamiliarTargetQuery()
        {
            m_DistanceComparison = CompareDistanceToSortOrigin;
        }

        public int CommandTargetCount => m_CommandTargets.Count;

        public void RefreshCache(Vector3 ownerPosition, float acquireRange, int maxTargets, LayerMask targetMask)
        {
            m_EnemyCache.Clear();
            EnsureScanCapacity(maxTargets);

            int count = Physics.OverlapSphereNonAlloc(
                ownerPosition,
                acquireRange,
                m_ScanBuffer,
                targetMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = m_ScanBuffer[i];
                if (candidate == null ||
                    !TryFindTarget(candidate, out MonoBehaviour behaviour, out _, out _)) continue;
                GameplayEntityId id = ResolveEntityId(behaviour);
                if (!id.IsNone) m_EnemyCache[id] = behaviour;
            }
        }

        public bool IsWithinAcquireRange(GameplayEntityId targetId, Vector3 ownerPosition, float acquireRange)
        {
            if (!m_EnemyCache.TryGetValue(targetId, out MonoBehaviour cached) || cached == null)
                return true;
            return (cached.transform.position - ownerPosition).sqrMagnitude <= acquireRange * acquireRange;
        }

        public bool TryResolveTarget(
            GameplayEntityId targetId,
            Vector3 ownerPosition,
            float acquireRange,
            out MonoBehaviour target)
        {
            target = null;
            if (targetId.IsNone ||
                !m_EnemyCache.TryGetValue(targetId, out MonoBehaviour cached) || cached == null) return false;
            if ((cached.transform.position - ownerPosition).sqrMagnitude > acquireRange * acquireRange) return false;
            target = cached;
            return true;
        }

        public bool QueryCommandTargets(Vector3 origin, float radius, int maxTargets, LayerMask targetMask)
        {
            EnsureCommandCapacity(maxTargets);
            int count = Physics.OverlapSphereNonAlloc(
                origin,
                radius,
                m_CommandBuffer,
                targetMask,
                QueryTriggerInteraction.Collide);

            m_CommandTargets.Clear();
            for (int i = 0; i < count && m_CommandTargets.Count < m_CommandBuffer.Length; i++)
            {
                Collider candidate = m_CommandBuffer[i];
                if (candidate != null) m_CommandTargets.Add(candidate);
            }
            if (m_CommandTargets.Count == 0) return false;

            m_SortOrigin = origin;
            m_CommandTargets.Sort(m_DistanceComparison);
            return true;
        }

        public GameplayEntityId GetCommandTargetId(int index)
        {
            if (index < 0 || index >= m_CommandTargets.Count) return GameplayEntityId.None;
            return ResolveEntityId(m_CommandTargets[index].transform);
        }

        public static GameplayEntityId ResolveEntityId(Component component)
        {
            if (component == null) return GameplayEntityId.None;
            ICombatEntityIdentity identity = component.GetComponentInParent<ICombatEntityIdentity>();
            return identity != null ? identity.CombatEntityId : GameplayEntityId.None;
        }

        public static bool TryFindTarget(
            Collider collider,
            out MonoBehaviour target,
            out ITrustedCombatHitTarget trustedTarget,
            out IHittable fallback)
        {
            MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is ITrustedCombatHitTarget trusted)) continue;
                target = behaviours[i];
                trustedTarget = trusted;
                fallback = null;
                return true;
            }
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IHittable hittable)) continue;
                target = behaviours[i];
                trustedTarget = null;
                fallback = hittable;
                return true;
            }
            target = null;
            trustedTarget = null;
            fallback = null;
            return false;
        }

        private void EnsureScanCapacity(int maxTargets)
        {
            if (m_ScanBuffer == null || m_ScanBuffer.Length < maxTargets)
                m_ScanBuffer = new Collider[Mathf.Max(1, maxTargets)];
        }

        private void EnsureCommandCapacity(int maxTargets)
        {
            if (m_CommandBuffer == null || m_CommandBuffer.Length < maxTargets)
                m_CommandBuffer = new Collider[Mathf.Max(1, maxTargets)];
        }

        private int CompareDistanceToSortOrigin(Collider a, Collider b)
        {
            float da = HorizontalSqrDistance(a.transform.position, m_SortOrigin);
            float db = HorizontalSqrDistance(b.transform.position, m_SortOrigin);
            return da.CompareTo(db);
        }

        private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
        {
            float dx = from.x - to.x;
            float dz = from.z - to.z;
            return dx * dx + dz * dz;
        }
    }
}
