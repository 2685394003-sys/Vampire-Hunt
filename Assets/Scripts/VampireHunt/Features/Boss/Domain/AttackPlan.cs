using System;
using System.Collections;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Boss.Domain
{
    public enum TelegraphShape
    {
        None = 0,
        Line = 1,
        Circle = 2,
        Cross = 3,
        Rectangle = 4,
        Radial = 5
    }

    public readonly struct TelegraphSpec
    {
        public TelegraphShape Shape { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public float Duration { get; }
        public float Range { get; }
        public float Width { get; }
        public PresentationCueId Cue { get; }

        public TelegraphSpec(
            TelegraphShape shape,
            WorldPosition origin,
            WorldPosition target,
            float duration,
            float range,
            float width,
            PresentationCueId cue = default)
        {
            Shape = shape;
            Origin = origin;
            Target = target;
            Duration = Math.Max(0f, duration);
            Range = Math.Max(0f, range);
            Width = Math.Max(0f, width);
            Cue = cue;
        }
    }

    public readonly struct DamageWindow
    {
        public float StartsAt { get; }
        public float Duration { get; }
        public TelegraphShape Shape { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public int Damage { get; }
        public float Range { get; }
        public float Width { get; }
        public float Knockback { get; }

        public DamageWindow(
            float startsAt,
            float duration,
            TelegraphShape shape,
            WorldPosition origin,
            WorldPosition target,
            int damage,
            float range,
            float width,
            float knockback = 0f)
        {
            StartsAt = Math.Max(0f, startsAt);
            Duration = Math.Max(0f, duration);
            Shape = shape;
            Origin = origin;
            Target = target;
            Damage = Math.Max(0, damage);
            Range = Math.Max(0f, range);
            Width = Math.Max(0f, width);
            Knockback = Math.Max(0f, knockback);
        }

        public bool IsActive(float elapsed) => elapsed >= StartsAt && elapsed < StartsAt + Duration;
    }

    public sealed class DamageWindowSet : IReadOnlyList<DamageWindow>
    {
        private readonly DamageWindow[] items;
        public static DamageWindowSet Empty { get; } = new(Array.Empty<DamageWindow>());
        public int Count => items.Length;
        public DamageWindow this[int index] => items[index];
        public DamageWindowSet(IEnumerable<DamageWindow> windows) =>
            items = windows == null ? Array.Empty<DamageWindow>() : new List<DamageWindow>(windows).ToArray();
        public IEnumerator<DamageWindow> GetEnumerator() => ((IEnumerable<DamageWindow>)items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }

    public enum MovementPlanKind
    {
        None = 0,
        Hold = 1,
        Approach = 2,
        Retreat = 3,
        Dash = 4
    }

    public readonly struct MovementPlan
    {
        public MovementPlanKind Kind { get; }
        public WorldPosition From { get; }
        public WorldPosition To { get; }
        public float StartsAt { get; }
        public float Duration { get; }
        public float Speed { get; }

        public MovementPlan(
            MovementPlanKind kind,
            WorldPosition from,
            WorldPosition to,
            float startsAt,
            float duration,
            float speed)
        {
            Kind = kind;
            From = from;
            To = to;
            StartsAt = Math.Max(0f, startsAt);
            Duration = Math.Max(0f, duration);
            Speed = Math.Max(0f, speed);
        }
    }

    public readonly struct ProjectileRequest
    {
        public float SpawnAt { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public int Count { get; }
        public int Damage { get; }
        public float Speed { get; }
        public float Lifetime { get; }
        public float RotationOffsetDegrees { get; }
        public float Knockback { get; }

        public ProjectileRequest(
            float spawnAt,
            WorldPosition origin,
            WorldPosition target,
            int count,
            int damage,
            float speed,
            float lifetime,
            float rotationOffsetDegrees = 0f,
            float knockback = 0f)
        {
            SpawnAt = Math.Max(0f, spawnAt);
            Origin = origin;
            Target = target;
            Count = Math.Max(0, count);
            Damage = Math.Max(0, damage);
            Speed = Math.Max(0f, speed);
            Lifetime = Math.Max(0f, lifetime);
            RotationOffsetDegrees = rotationOffsetDegrees;
            Knockback = Math.Max(0f, knockback);
        }
    }

    public sealed class ProjectileRequestSet : IReadOnlyList<ProjectileRequest>
    {
        private readonly ProjectileRequest[] items;
        public static ProjectileRequestSet Empty { get; } = new(Array.Empty<ProjectileRequest>());
        public int Count => items.Length;
        public ProjectileRequest this[int index] => items[index];
        public ProjectileRequestSet(IEnumerable<ProjectileRequest> requests) =>
            items = requests == null ? Array.Empty<ProjectileRequest>() : new List<ProjectileRequest>(requests).ToArray();
        public IEnumerator<ProjectileRequest> GetEnumerator() => ((IEnumerable<ProjectileRequest>)items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }

    public readonly struct AttackPlan
    {
        public BossAttackId AttackId { get; }
        public TelegraphSpec Telegraph { get; }
        public DamageWindowSet DamageWindows { get; }
        public MovementPlan Movement { get; }
        public ProjectileRequestSet Projectiles { get; }
        public float Duration { get; }

        public AttackPlan(
            BossAttackId attackId,
            TelegraphSpec telegraph,
            DamageWindowSet damageWindows,
            MovementPlan movement,
            ProjectileRequestSet projectiles,
            float duration)
        {
            if (attackId == BossAttackId.None) throw new ArgumentOutOfRangeException(nameof(attackId));
            AttackId = attackId;
            Telegraph = telegraph;
            DamageWindows = damageWindows ?? DamageWindowSet.Empty;
            Movement = movement;
            Projectiles = projectiles ?? ProjectileRequestSet.Empty;
            Duration = Math.Max(0f, duration);
        }
    }

    public readonly struct BossAttackContext
    {
        public EntityId BossId { get; }
        public BossPhase Phase { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public double Now { get; }

        public BossAttackContext(EntityId bossId, BossPhase phase, WorldPosition origin, WorldPosition target, double now)
        {
            BossId = bossId;
            Phase = phase;
            Origin = origin;
            Target = target;
            Now = now;
        }
    }

    public interface IBossAttackStrategy
    {
        BossAttackId AttackId { get; }
        AttackPlan BuildPlan(BossAttackContext context, BossAttackSpec spec);
    }
}
