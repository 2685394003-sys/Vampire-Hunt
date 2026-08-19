using System;
using VampireHunt.Boss.Contracts;

namespace VampireHunt.Boss.Domain
{
    internal abstract class BossAttackStrategyBase : IBossAttackStrategy
    {
        public abstract BossAttackId AttackId { get; }
        protected abstract TelegraphShape Shape { get; }

        public AttackPlan BuildPlan(BossAttackContext context, BossAttackSpec spec)
        {
            if (spec.Id != AttackId)
                throw new ArgumentException($"{GetType().Name} cannot build '{spec.Id}'.", nameof(spec));
            return Build(context, spec);
        }

        protected abstract AttackPlan Build(BossAttackContext context, BossAttackSpec spec);

        protected TelegraphSpec Telegraph(BossAttackContext context, BossAttackSpec spec) =>
            new(Shape, context.Origin, context.Target, spec.TelegraphSeconds, spec.Range, spec.Width, spec.Cue);

        protected DamageWindow Window(BossAttackContext context, BossAttackSpec spec, TelegraphShape shape) =>
            new(spec.TelegraphSeconds, Math.Max(0.01f, spec.ActiveSeconds), shape,
                context.Origin, context.Target, spec.Damage, spec.Range, spec.Width, spec.Knockback);
    }

    internal sealed class GuardSweepAttack : BossAttackStrategyBase
    {
        public override BossAttackId AttackId => BossAttackId.GuardSweep;
        protected override TelegraphShape Shape => TelegraphShape.Line;

        protected override AttackPlan Build(BossAttackContext context, BossAttackSpec spec) =>
            new(AttackId, Telegraph(context, spec),
                new DamageWindowSet(new[] { Window(context, spec, Shape) }),
                new MovementPlan(MovementPlanKind.Hold, context.Origin, context.Origin, 0f,
                    spec.TelegraphSeconds + spec.ActiveSeconds, 0f),
                ProjectileRequestSet.Empty,
                spec.TelegraphSeconds + spec.ActiveSeconds);
    }

    internal sealed class RotatingBarrageAttack : BossAttackStrategyBase
    {
        public override BossAttackId AttackId => BossAttackId.RotatingBarrage;
        protected override TelegraphShape Shape => TelegraphShape.Radial;

        protected override AttackPlan Build(BossAttackContext context, BossAttackSpec spec)
        {
            ProjectileRequest request = new(
                spec.TelegraphSeconds,
                context.Origin,
                context.Target,
                Math.Max(1, spec.ProjectileCount),
                spec.Damage,
                spec.ProjectileSpeed,
                Math.Max(0.1f, spec.ActiveSeconds),
                17f,
                spec.Knockback);
            return new AttackPlan(
                AttackId,
                Telegraph(context, spec),
                DamageWindowSet.Empty,
                new MovementPlan(MovementPlanKind.Hold, context.Origin, context.Origin, 0f,
                    spec.TelegraphSeconds + spec.ActiveSeconds, 0f),
                new ProjectileRequestSet(new[] { request }),
                spec.TelegraphSeconds + spec.ActiveSeconds);
        }
    }

    internal sealed class CrossSlashAttack : BossAttackStrategyBase
    {
        public override BossAttackId AttackId => BossAttackId.CrossSlash;
        protected override TelegraphShape Shape => TelegraphShape.Cross;

        protected override AttackPlan Build(BossAttackContext context, BossAttackSpec spec) =>
            new(AttackId, Telegraph(context, spec),
                new DamageWindowSet(new[] { Window(context, spec, Shape) }),
                new MovementPlan(MovementPlanKind.Hold, context.Origin, context.Origin, 0f,
                    spec.TelegraphSeconds + spec.ActiveSeconds, 0f),
                ProjectileRequestSet.Empty,
                spec.TelegraphSeconds + spec.ActiveSeconds);
    }

    internal sealed class ChargedSlashAttack : BossAttackStrategyBase
    {
        public override BossAttackId AttackId => BossAttackId.ChargedSlash;
        protected override TelegraphShape Shape => TelegraphShape.Line;

        protected override AttackPlan Build(BossAttackContext context, BossAttackSpec spec) =>
            new(AttackId, Telegraph(context, spec),
                new DamageWindowSet(new[] { Window(context, spec, Shape) }),
                new MovementPlan(MovementPlanKind.Hold, context.Origin, context.Origin, 0f,
                    spec.TelegraphSeconds + spec.ActiveSeconds, 0f),
                ProjectileRequestSet.Empty,
                spec.TelegraphSeconds + spec.ActiveSeconds);
    }

    internal sealed class RectangleDashAttack : BossAttackStrategyBase
    {
        public override BossAttackId AttackId => BossAttackId.RectangleDash;
        protected override TelegraphShape Shape => TelegraphShape.Rectangle;

        protected override AttackPlan Build(BossAttackContext context, BossAttackSpec spec) =>
            new(AttackId, Telegraph(context, spec),
                new DamageWindowSet(new[] { Window(context, spec, Shape) }),
                new MovementPlan(MovementPlanKind.Dash, context.Origin, context.Target,
                    spec.TelegraphSeconds, spec.ActiveSeconds, spec.ProjectileSpeed),
                ProjectileRequestSet.Empty,
                spec.TelegraphSeconds + spec.ActiveSeconds);
    }
}
