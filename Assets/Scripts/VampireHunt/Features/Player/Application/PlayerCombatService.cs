using System;
using System.Collections.Generic;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Abilities.Domain;
using VampireHunt.Stats;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Application
{
    /// <summary>
    /// Resolves one unique target per attack. The resolver is the Combat boundary;
    /// it owns critical rolls and actual damage, while this service owns attack
    /// timing and target de-duplication.
    /// </summary>
    public sealed class PlayerCombatService
    {
        private readonly IMeleeHitQuery hitQuery;
        private readonly IPlayerDamageResolver damageResolver;
        private readonly IPlayerPositionQuery positions;
        private readonly float attackRange;
        private readonly float attackConeAngle;
        private readonly double attackWindowDuration;
        public GameplayAbilitySystem AbilitySystem { get; }

        public PlayerCombatService(
            IMeleeHitQuery hitQuery,
            CombatApplicationService combat,
            GameplayAbilitySystem abilitySystem,
            IPlayerPositionQuery positions,
            float attackRange,
            float attackConeAngle,
            double attackWindowDuration = 0d,
            Func<EntityId, IStatSnapshot> sourceStats = null)
            : this(
                hitQuery,
                new CombatDamageResolverAdapter(combat, sourceStats),
                positions,
                attackRange,
                attackConeAngle,
                attackWindowDuration)
        {
            AbilitySystem = abilitySystem;
        }

        public PlayerCombatService(
            IMeleeHitQuery hitQuery,
            CombatApplicationService combat,
            IPlayerPositionQuery positions,
            float attackRange,
            float attackConeAngle,
            double attackWindowDuration = 0d,
            Func<EntityId, IStatSnapshot> sourceStats = null)
            : this(
                hitQuery,
                new CombatDamageResolverAdapter(combat, sourceStats),
                positions,
                attackRange,
                attackConeAngle,
                attackWindowDuration)
        {
            AbilitySystem = null;
        }

        public PlayerCombatService(
            IMeleeHitQuery hitQuery,
            IPlayerDamageResolver damageResolver,
            IPlayerPositionQuery positions,
            float attackRange,
            float attackConeAngle,
            double attackWindowDuration = 0d)
        {
            this.hitQuery = hitQuery ?? throw new ArgumentNullException(nameof(hitQuery));
            this.damageResolver = damageResolver ?? throw new ArgumentNullException(nameof(damageResolver));
            this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
            this.attackRange = Math.Max(0f, attackRange);
            this.attackConeAngle = Math.Max(0f, attackConeAngle);
            this.attackWindowDuration = Math.Max(0d, attackWindowDuration);
            AbilitySystem = null;
        }

        public AttackResult Attack(PlayerAggregate player, AttackCommand command, double now)
        {
            if (player == null || !player.IsAlive)
                return new AttackResult(false, "Player is not alive", 0, null);
            if (command.PlayerId != player.Id)
                return new AttackResult(false, "Player id mismatch", 0, null);
            if (!MovementPose.IsFinitePosition(command.AimAt))
                return new AttackResult(false, "Aim position is invalid", 0, null);
            if (!player.TryBeginAttack(now, attackWindowDuration))
                return new AttackResult(false, "Attack is on cooldown", 0, null);

            List<IMeleeHitTarget> targets = new();
            List<DamageResult> results = new();
            try
            {
                if (!positions.TryGetPosition(player.Id, out WorldPosition origin))
                    return new AttackResult(false, "Player position is unavailable", 0, null);

                hitQuery.CollectUniqueTargets(
                    new MeleeHitQuery(
                        player.Id,
                        origin,
                        command.AimAt,
                        attackRange,
                        attackConeAngle),
                    targets);

                HashSet<EntityId> seen = new();
                int considered = 0;
                int baseDamage = Math.Max(0, (int)Math.Round(player.RunStats.GetValue(PlayerStat.BaseAttack)));
                for (int i = 0; i < targets.Count; i++)
                {
                    IMeleeHitTarget target = targets[i];
                    if (target == null || !target.IsAlive || !seen.Add(target.Id)) continue;
                    considered++;
                    results.Add(damageResolver.Apply(new PlayerAttackRequest(
                        player.Id,
                        target.Id,
                        baseDamage,
                        command.AimAt,
                        target.HitPosition)));
                }

                return new AttackResult(true, string.Empty, considered, results);
            }
            finally
            {
                player.CombatState.CompleteAttack();
            }
        }

        public AttackResult Attack(PlayerAggregate player, AttackCommand command) =>
            Attack(player, command, 0d);

        public AttackResult Attack(PlayerAggregate player, AttackCommand command, IGameClock clock) =>
            Attack(player, command, clock == null ? 0d : clock.Now);
    }

    public sealed class CombatDamageResolverAdapter : IPlayerDamageResolver
    {
        private readonly CombatApplicationService combat;
        private readonly Func<EntityId, IStatSnapshot> sourceStats;

        public CombatDamageResolverAdapter(
            CombatApplicationService combat,
            Func<EntityId, IStatSnapshot> sourceStats = null)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.sourceStats = sourceStats;
        }

        public DamageResult Apply(PlayerAttackRequest request)
        {
            DamageRequest damage = new(
                request.SourceId,
                request.TargetId,
                request.BaseDamage,
                DamageFlags.None,
                new HitContext(request.HitPosition));
            return combat.ApplyDamage(damage, sourceStats?.Invoke(request.SourceId));
        }
    }
}
