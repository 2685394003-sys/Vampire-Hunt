using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Enemies.Contracts
{
    /// <summary>Server-side attack intent produced by an enemy policy.</summary>
    public readonly struct AttackIntent
    {
        public AttackIntent(
            EntityId sourceId,
            EntityId targetId,
            EnemyAttackType attackType,
            int baseDamage,
            WorldPosition hitPosition,
            DamageTag damageTag = default(DamageTag),
            DamageFlags flags = DamageFlags.None)
        {
            SourceId = sourceId;
            TargetId = targetId;
            AttackType = attackType;
            BaseDamage = baseDamage < 0 ? 0 : baseDamage;
            HitPosition = hitPosition;
            DamageTag = damageTag;
            Flags = flags;
        }

        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public EnemyAttackType AttackType { get; }
        public int BaseDamage { get; }
        public WorldPosition HitPosition { get; }
        public DamageTag DamageTag { get; }
        public DamageFlags Flags { get; }
        public bool IsRanged => AttackType == EnemyAttackType.Ranged;
    }
}
