using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Enemies.Contracts
{
    /// <summary>
    /// Public adapter surface for a pooled enemy view.  The view never receives
    /// the mutable EnemyAggregate; it only receives immutable snapshots and
    /// intent/results.
    /// </summary>
    public interface IEnemyRuntime
    {
        EntityId Id { get; }
        bool IsSpawned { get; }
        float MoveSpeed { get; }
        EnemySnapshot Snapshot { get; }
        IDamageReceiver DamageReceiver { get; }
        IHealingReceiver HealingReceiver { get; }

        void ResetForSpawn(EnemySpec spec);
        void ResetForSpawn(EntityId freshId, EnemySpec spec);
        void ResetForDespawn();
        DamageResult ApplyDamage(in ResolvedDamage damage);
        int ApplyHealing(int amount);
        EnemyPerceptionData Perceive(float radius);
        EnemyIntent Decide();
        EnemyIntent Decide(in EnemyPerceptionData perception);
        AttackIntent CreateAttackIntent(in EnemyCombatContextData context);
        EnemyDeathResultData SettleDeath(EntityId killerId);
        void SetPosition(WorldPosition position);
        void SetState(EnemyState state);
        void RecordAttack(double occurredAt);
    }

    /// <summary>Immutable perception DTO accepted by the runtime facade.</summary>
    public readonly struct EnemyPerceptionData
    {
        public EnemyPerceptionData(
            EntityId selfId,
            WorldPosition selfPosition,
            EntityId targetId,
            WorldPosition targetPosition,
            float distance,
            bool hasLineOfTravel,
            bool targetIsAlive)
        {
            SelfId = selfId;
            SelfPosition = selfPosition;
            TargetId = targetId;
            TargetPosition = targetPosition;
            Distance = distance < 0f ? 0f : distance;
            HasLineOfTravel = hasLineOfTravel;
            TargetIsAlive = targetIsAlive;
        }

        public EntityId SelfId { get; }
        public WorldPosition SelfPosition { get; }
        public EntityId TargetId { get; }
        public WorldPosition TargetPosition { get; }
        public float Distance { get; }
        public bool HasLineOfTravel { get; }
        public bool TargetIsAlive { get; }

        public static EnemyPerceptionData NoTarget(
            EntityId selfId,
            WorldPosition selfPosition) =>
            new EnemyPerceptionData(
                selfId,
                selfPosition,
                default(EntityId),
                default(WorldPosition),
                0f,
                false,
                false);
    }

    /// <summary>
    /// Public composition-root port for Unity views. Bootstrap discovers this
    /// port on a prefab without referencing the legacy Assembly-CSharp type
    /// and binds the already-composed runtime before simulation begins.
    /// </summary>
    public interface IEnemyRuntimeBinding
    {
        IEnemyRuntime Runtime { get; }
        bool TryBind(IEnemyRuntime runtime);
        bool TryBind(IEnemyRuntime runtime, EnemySpec spec);
    }

    /// <summary>
    /// Logical release port for a pooled enemy life. It deliberately carries
    /// only EntityId so Contracts never expose GameObject/NetworkObject.
    /// </summary>
    public interface IEnemyLifetimePort
    {
        void Release(EntityId entityId);
    }

    public interface IEnemyLifetimeBinding
    {
        bool TryBind(IEnemyLifetimePort lifetime);
    }

    /// <summary>View reset callbacks invoked by the pool adapter.</summary>
    public interface IEnemyPoolLifecycle
    {
        void OnTakenFromEnemyPool();
        void OnReturnedToEnemyPool();
    }

    /// <summary>
    /// Immutable context DTO used by adapters that do not need to expose the
    /// internal EnemyCombatContext type.
    /// </summary>
    public readonly struct EnemyCombatContextData
    {
        public EnemyCombatContextData(
            EntityId sourceId,
            EntityId targetId,
            WorldPosition hitPosition,
            float distance,
            bool targetIsAlive,
            double now,
            double lastAttackAt)
        {
            SourceId = sourceId;
            TargetId = targetId;
            HitPosition = hitPosition;
            Distance = distance;
            TargetIsAlive = targetIsAlive;
            Now = now;
            LastAttackAt = lastAttackAt;
        }

        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public WorldPosition HitPosition { get; }
        public float Distance { get; }
        public bool TargetIsAlive { get; }
        public double Now { get; }
        public double LastAttackAt { get; }
    }

    /// <summary>Result returned by the public death adapter.</summary>
    public readonly struct EnemyDeathResultData
    {
        public EnemyDeathResultData(
            bool settled,
            EntityId enemyId,
            EntityId killerId,
            RewardGrant reward)
        {
            Settled = settled;
            EnemyId = enemyId;
            KillerId = killerId;
            Reward = reward;
        }

        public bool Settled { get; }
        public EntityId EnemyId { get; }
        public EntityId KillerId { get; }
        public RewardGrant Reward { get; }
    }

    /// <summary>
    /// Narrow adapter port for a legacy attack view.  The implementation is
    /// supplied by Integration/Netcode and is responsible for routing the
    /// intent to EnemyCombatService/CombatApplicationService.
    /// </summary>
    public interface IEnemyAttackPort
    {
        bool Execute(in AttackIntent intent);
    }

    /// <summary>Projectile adapter port; spawning/transport stays outside Domain.</summary>
    public interface IEnemyProjectilePort
    {
        bool Spawn(in AttackIntent intent);
    }

    /// <summary>Server projectile hit adapter port; it forwards to Combat.</summary>
    public interface IEnemyProjectileHitPort
    {
        bool ApplyHit(EntityId sourceId, EntityId targetId, WorldPosition position);
    }
}
