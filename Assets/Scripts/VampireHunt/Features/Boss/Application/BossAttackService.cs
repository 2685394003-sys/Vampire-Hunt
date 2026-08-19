using System;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;

namespace VampireHunt.Boss.Application
{
    public readonly struct AttackStartResult
    {
        public bool Started { get; }
        public BossAttackId AttackId { get; }
        public string Reason { get; }

        public AttackStartResult(bool started, BossAttackId attackId, string reason = null)
        {
            Started = started;
            AttackId = attackId;
            Reason = reason ?? string.Empty;
        }
    }

    public sealed class BossAttackService
    {
        private readonly IBossRepository repository;
        private readonly Dictionary<BossAttackId, IBossAttackStrategy> strategies;
        private readonly IBossMotor motor;
        private readonly IAttackWorldQuery worldQuery;
        private readonly IProjectileSpawner projectileSpawner;
        private readonly IBossWorldState worldState;
        private readonly CombatApplicationService combat;
        private readonly KnockbackResolver knockback;
        private readonly IGameplayEventSink eventSink;
        private readonly IGameClock clock;
        private readonly GameplayEventIdAllocator eventIds;
        private readonly Dictionary<EntityId, ExecutionTracker> trackers = new();
        private readonly List<ICombatTarget> targetBuffer = new(32);

        internal BossAttackService(
            IBossRepository repository,
            IBossMotor motor,
            IAttackWorldQuery worldQuery,
            IProjectileSpawner projectileSpawner,
            IBossWorldState worldState,
            CombatApplicationService combat,
            IGameplayEventSink eventSink,
            IGameClock clock,
            GameplayEventIdAllocator eventIds)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.worldQuery = worldQuery ?? throw new ArgumentNullException(nameof(worldQuery));
            this.projectileSpawner = projectileSpawner ?? throw new ArgumentNullException(nameof(projectileSpawner));
            this.worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.eventSink = eventSink;
            this.clock = clock;
            this.eventIds = eventIds ?? new GameplayEventIdAllocator();
            knockback = new KnockbackResolver();
            strategies = CreateStrategies();
        }

        public AttackStartResult TryStartAttack(EntityId bossId)
        {
            return TryStartAttack(bossId, BossAttackId.None);
        }

        /// <summary>
        /// Starts the domain-selected attack, or a specifically requested
        /// attack for a validated legacy/debug command. Selection, cooldown,
        /// phase gating and plan construction still happen in the Boss
        /// application/domain layer; Unity adapters never recreate them.
        /// </summary>
        public AttackStartResult TryStartAttack(EntityId bossId, BossAttackId requestedAttack)
        {
            BossAggregate boss = repository.Get(bossId);
            if (!boss.Vitals.IsAlive || boss.Encounter.Mode is EncounterMode.Inactive or EncounterMode.Defeated)
                return new AttackStartResult(false, BossAttackId.None, "Encounter is not active.");
            if (boss.Attacks.IsExecuting)
                return new AttackStartResult(false, boss.Attacks.CurrentAttack, "An attack is already executing.");
            if (!worldState.TryGetAttackContext(
                    bossId, boss.Phases.CurrentPhase, clock?.Now ?? 0d, out BossAttackContext context))
                return new AttackStartResult(false, BossAttackId.None, "No authoritative target pose is available.");

            BossAttackId attackId = requestedAttack == BossAttackId.None
                ? boss.AttackSelector.Select(
                    new BossAttackSelectionContext(boss.Phases.CurrentPhase, boss.Attacks))
                : requestedAttack;
            if (attackId == BossAttackId.None)
                return new AttackStartResult(false, attackId, "No configured attack is ready.");

            if (!boss.Spec.Attacks.TryGet(attackId, out BossAttackSpec selectedSpec) ||
                (int)boss.Phases.CurrentPhase < (int)selectedSpec.MinimumPhase ||
                selectedSpec.Weight <= 0f ||
                !boss.Attacks.Cooldowns.IsReady(attackId))
                return new AttackStartResult(false, attackId, "Requested attack is not ready for this phase.");

            BossAttackSpec spec = selectedSpec;
            AttackPlan plan = strategies[attackId].BuildPlan(context, spec);
            if (!boss.Attacks.TryBegin(plan, spec.Cooldown))
                return new AttackStartResult(false, attackId, "Attack state rejected the plan.");

            trackers[bossId] = new ExecutionTracker(plan.DamageWindows.Count, plan.Projectiles.Count);
            motor.Execute(bossId, plan.Movement);
            PublishTelegraph(bossId, plan);
            PublishCue(bossId, attackId, BossAttackCuePhase.Started, spec.Cue);
            return new AttackStartResult(true, attackId);
        }

        public void CancelAttack(EntityId bossId) => Cancel(bossId);

        public void TickAttack(EntityId bossId, float deltaTime)
        {
            BossAggregate boss = repository.Get(bossId);
            if (!boss.Attacks.IsExecuting)
            {
                boss.Attacks.Cooldowns.Tick(Math.Max(0f, deltaTime));
                return;
            }

            AttackPlan plan = boss.Attacks.CurrentPlan;
            float previousElapsed = boss.Attacks.Elapsed;
            float nextElapsed = Math.Min(plan.Duration, previousElapsed + Math.Max(0f, deltaTime));
            if (!trackers.TryGetValue(bossId, out ExecutionTracker tracker))
            {
                tracker = new ExecutionTracker(plan.DamageWindows.Count, plan.Projectiles.Count);
                trackers[bossId] = tracker;
            }

            for (int i = 0; i < plan.DamageWindows.Count; i++)
            {
                DamageWindow window = plan.DamageWindows[i];
                if (!tracker.WindowExecuted[i] && Crossed(previousElapsed, nextElapsed, window.StartsAt))
                {
                    ExecuteWindow(bossId, window);
                    tracker.WindowExecuted[i] = true;
                }
            }

            for (int i = 0; i < plan.Projectiles.Count; i++)
            {
                ProjectileRequest request = plan.Projectiles[i];
                if (!tracker.ProjectileSpawned[i] && Crossed(previousElapsed, nextElapsed, request.SpawnAt))
                {
                    projectileSpawner.Spawn(bossId, plan.AttackId, request);
                    tracker.ProjectileSpawned[i] = true;
                }
            }

            bool completed = boss.Attacks.Tick(Math.Max(0f, deltaTime));
            if (completed)
            {
                BossAttackSpec spec = boss.Spec.Attacks.Get(plan.AttackId);
                PublishCue(bossId, plan.AttackId, BossAttackCuePhase.Completed, spec.Cue);
                trackers.Remove(bossId);
            }
        }

        public void Cancel(EntityId bossId)
        {
            BossAggregate boss = repository.Get(bossId);
            if (!boss.Attacks.IsExecuting) return;
            BossAttackId attackId = boss.Attacks.CurrentAttack;
            PresentationCueId cue = boss.Spec.Attacks.Get(attackId).Cue;
            boss.Attacks.Cancel();
            trackers.Remove(bossId);
            PublishCue(bossId, attackId, BossAttackCuePhase.Cancelled, cue);
        }

        private void ExecuteWindow(EntityId bossId, in DamageWindow window)
        {
            targetBuffer.Clear();
            worldQuery.CollectTargets(bossId, window, targetBuffer);
            HashSet<EntityId> unique = new();
            for (int i = 0; i < targetBuffer.Count; i++)
            {
                ICombatTarget target = targetBuffer[i];
                if (target == null || !target.IsAlive || !unique.Add(target.Id)) continue;
                DamageRequest request = new(
                    bossId,
                    target.Id,
                    window.Damage,
                    DamageFlags.NoCritical,
                    new HitContext(target.Position, new DamageTag("boss")));
                combat.ApplyDamage(in request);

                if (window.Knockback > 0f)
                {
                    KnockbackRequest knockbackRequest = new(
                        bossId,
                        target.Id,
                        target.Position - window.Origin,
                        window.Knockback,
                        0.15f);
                    combat.ApplyKnockback(in knockbackRequest, knockback);
                }
            }
        }

        private void PublishTelegraph(EntityId bossId, in AttackPlan plan)
        {
            eventSink?.Publish(new BossAttackTelegraphEvent(
                eventIds.Next(), clock?.Now ?? 0d, bossId, plan.AttackId,
                plan.Telegraph.Origin, plan.Telegraph.Target, plan.Telegraph.Duration, plan.Telegraph.Cue));
        }

        private void PublishCue(
            EntityId bossId,
            BossAttackId attackId,
            BossAttackCuePhase phase,
            PresentationCueId cue)
        {
            eventSink?.Publish(new BossAttackCueEvent(
                eventIds.Next(), clock?.Now ?? 0d, bossId, attackId, phase, cue));
        }

        private static bool Crossed(float previous, float current, float scheduled) =>
            scheduled <= current && (scheduled > previous || previous <= 0f && scheduled <= 0f);

        private static Dictionary<BossAttackId, IBossAttackStrategy> CreateStrategies() => new()
        {
            [BossAttackId.GuardSweep] = new GuardSweepAttack(),
            [BossAttackId.RotatingBarrage] = new RotatingBarrageAttack(),
            [BossAttackId.CrossSlash] = new CrossSlashAttack(),
            [BossAttackId.ChargedSlash] = new ChargedSlashAttack(),
            [BossAttackId.RectangleDash] = new RectangleDashAttack()
        };

        private sealed class ExecutionTracker
        {
            public bool[] WindowExecuted { get; }
            public bool[] ProjectileSpawned { get; }
            public ExecutionTracker(int windowCount, int projectileCount)
            {
                WindowExecuted = new bool[windowCount];
                ProjectileSpawned = new bool[projectileCount];
            }
        }
    }
}
