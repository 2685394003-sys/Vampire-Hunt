using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Server-side data supplied by an enemy actor to a concrete attack adapter.</summary>
    public readonly struct EnemyAttackExecutionContext
    {
        public GameplayEntityId Source { get; }
        public uint AttackId { get; }
        public uint AttackSequence { get; }
        public float Damage { get; }
        public float Knockback { get; }
        public float MaximumTargetDistance { get; }
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public NetworkObject Target { get; }

        public EnemyAttackExecutionContext(
            GameplayEntityId source,
            uint attackId,
            uint attackSequence,
            float damage,
            float knockback,
            float maximumTargetDistance,
            Vector3 origin,
            Vector3 direction,
            NetworkObject target)
        {
            Source = source;
            AttackId = attackId;
            AttackSequence = attackSequence;
            Damage = Mathf.Max(0f, damage);
            Knockback = Mathf.Max(0f, knockback);
            MaximumTargetDistance = Mathf.Max(0.1f, maximumTargetDistance);
            Origin = origin;
            Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Target = target;
        }
    }

    public interface IEnemyAttackExecutor
    {
        bool TryExecute(in EnemyAttackExecutionContext context);
    }

    /// <summary>Combat payload configured before a pooled projectile is network-spawned.</summary>
    public readonly struct ServerCombatProjectilePayload
    {
        public GameplayEntityId Source { get; }
        public uint AttackId { get; }
        public ulong Sequence { get; }
        public float Damage { get; }
        public float Knockback { get; }
        public DamageTags Tags { get; }

        public ServerCombatProjectilePayload(
            GameplayEntityId source,
            uint attackId,
            ulong sequence,
            float damage,
            float knockback,
            DamageTags tags)
        {
            Source = source;
            AttackId = attackId;
            Sequence = sequence;
            Damage = Mathf.Max(0f, damage);
            Knockback = Mathf.Max(0f, knockback);
            Tags = tags;
        }
    }

    public interface IServerCombatProjectilePayloadReceiver
    {
        void ConfigureServer(in ServerCombatProjectilePayload payload);
    }
}
