using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Enemies.Contracts;

namespace VampireHunt.Enemies.Domain
{
    public sealed class EnemyCombatPolicy
    {
        private readonly float _attackRange;
        private readonly float _attackCooldown;
        private readonly int _attackDamage;
        private readonly EnemyAttackType _attackType;
        private readonly DamageTag _damageTag;
        private readonly DamageFlags _damageFlags;

        public EnemyCombatPolicy(
            float attackRange,
            float attackCooldown,
            int attackDamage,
            EnemyAttackType attackType,
            DamageTag damageTag = default(DamageTag),
            DamageFlags damageFlags = DamageFlags.None)
        {
            if (attackRange < 0f) throw new ArgumentOutOfRangeException(nameof(attackRange));
            if (attackCooldown < 0f) throw new ArgumentOutOfRangeException(nameof(attackCooldown));
            if (attackDamage < 0) throw new ArgumentOutOfRangeException(nameof(attackDamage));
            _attackRange = attackRange;
            _attackCooldown = attackCooldown;
            _attackDamage = attackDamage;
            _attackType = attackType;
            _damageTag = damageTag;
            _damageFlags = damageFlags;
        }

        public bool CanAttack(in EnemyCombatContext context)
        {
            if (!context.TargetId.IsValid || !context.TargetIsAlive) return false;
            if (context.Distance > _attackRange) return false;
            return context.Now - context.LastAttackAt >= _attackCooldown;
        }

        public bool CanAttack(EnemyCombatContext context) => CanAttack(in context);

        public AttackIntent CreateAttackIntent(in EnemyCombatContext context)
        {
            if (!CanAttack(context)) return default(AttackIntent);
            return new AttackIntent(
                context.SourceId,
                context.TargetId,
                _attackType,
                _attackDamage,
                context.HitPosition,
                _damageTag,
                _damageFlags);
        }

        public AttackIntent CreateAttackIntent(EnemyCombatContext context) => CreateAttackIntent(in context);

        public float AttackCooldown => _attackCooldown;
    }
}
