using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// 服务器入口的兼容期技能请求守门器。当前协议仍携带 Owner 侧生成的施法计划，
    /// 本类集中处理权限、有限数、位置和极端边界检查，避免校验散落到每个执行器。
    /// </summary>
    internal sealed class CombatAbilityRequestValidator
    {
        private readonly float m_MaxOriginDistance;
        private readonly float m_MaxScalar;
        private readonly int m_MaxProjectileCount;
        private readonly int m_MaxPierceCount;

        public CombatAbilityRequestValidator(
            float maxOriginDistance,
            float maxScalar,
            int maxProjectileCount,
            int maxPierceCount)
        {
            m_MaxOriginDistance = Mathf.Max(1f, maxOriginDistance);
            m_MaxScalar = Mathf.Max(1f, maxScalar);
            m_MaxProjectileCount = Mathf.Max(1, maxProjectileCount);
            m_MaxPierceCount = Mathf.Max(1, maxPierceCount);
        }

        public bool IsValid(
            NetworkObject playerObject,
            ulong senderClientId,
            in AbilityCastNetworkMessage message,
            out string rejectionReason)
        {
            if (playerObject == null || !playerObject.IsSpawned)
                return Reject("player object is not spawned", out rejectionReason);
            if (playerObject.OwnerClientId != senderClientId)
                return Reject("sender does not own this player", out rejectionReason);
            if (message.AbilityId == 0)
                return Reject("ability id is empty", out rejectionReason);
            if (!IsFinite(message.Origin) || !IsFinite(message.Direction))
                return Reject("origin or direction is not finite", out rejectionReason);
            if ((message.Origin - playerObject.transform.position).sqrMagnitude >
                m_MaxOriginDistance * m_MaxOriginDistance)
                return Reject("cast origin is too far from the authoritative player", out rejectionReason);
            if (!WithinScalarBounds(message))
                return Reject("one or more gameplay values are outside compatibility bounds", out rejectionReason);

            rejectionReason = null;
            return true;
        }

        private bool WithinScalarBounds(in AbilityCastNetworkMessage message)
        {
            return IsFiniteNonNegative(message.Damage) && message.Damage <= m_MaxScalar &&
                   IsFiniteNonNegative(message.TravelDistance) && message.TravelDistance <= m_MaxScalar &&
                   IsFiniteNonNegative(message.ProjectileSpeed) && message.ProjectileSpeed <= m_MaxScalar &&
                   IsFiniteNonNegative(message.Knockback) && message.Knockback <= m_MaxScalar &&
                   IsFiniteNonNegative(message.ProjectileSize) && message.ProjectileSize <= m_MaxScalar &&
                   IsFiniteNonNegative(message.SpreadAngle) && message.SpreadAngle <= 360f &&
                   IsFiniteNonNegative(message.FanAngle) && message.FanAngle <= 360f &&
                   message.ProjectileCount >= 0 && message.ProjectileCount <= m_MaxProjectileCount &&
                   message.PierceCount >= 0 && message.PierceCount <= m_MaxPierceCount;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFiniteNonNegative(float value) => IsFinite(value) && value >= 0f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Reject(string reason, out string rejectionReason)
        {
            rejectionReason = reason;
            return false;
        }
    }
}
