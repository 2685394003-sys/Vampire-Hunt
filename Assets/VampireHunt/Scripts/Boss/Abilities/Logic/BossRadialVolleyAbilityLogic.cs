using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossRadialVolleyAbilityLogic : BossGameplayAbilityLogic
    {
        private int m_SpawnedProjectileCount;
        private int m_TotalProjectileCount;
        private int m_ArmCount;
        private double m_NextShotTime;
        private double m_EndTime;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            m_SpawnedProjectileCount = 0;
            m_ArmCount = context.Modifiers.ParticipantCount <= 1
                ? Tuning.VolleyArmCount
                : System.Math.Min(64, 1 + context.Modifiers.QuantityUnits);
            int authoredWaves = System.Math.Max(
                1,
                (Tuning.ProjectileCount + Tuning.VolleyArmCount - 1) / Tuning.VolleyArmCount);
            m_TotalProjectileCount = context.Modifiers.ParticipantCount <= 1
                ? Tuning.ProjectileCount
                : authoredWaves * m_ArmCount;
            m_NextShotTime = 0d;
            m_EndTime = 0d;
        }

        protected override void Resolve(double serverTime)
        {
            if (Services.ProjectileSpawner == null || Services.BossBodyState == null) return;
            m_EndTime = serverTime + Tuning.Duration;
            SpawnWave(serverTime);
            m_NextShotTime = serverTime + Tuning.Interval;
        }

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve || m_SpawnedProjectileCount >= m_TotalProjectileCount ||
                serverTime > m_EndTime || m_NextShotTime <= 0d) return;

            // Catch up deterministically after a slow frame, but cap work per frame so a lag
            // spike cannot create an unbounded projectile burst.
            int catchUpWaves = 0;
            while (serverTime >= m_NextShotTime && m_SpawnedProjectileCount < m_TotalProjectileCount &&
                   m_NextShotTime <= m_EndTime && catchUpWaves++ < 8)
            {
                SpawnWave(m_NextShotTime);
                m_NextShotTime += Tuning.Interval;
            }
        }

        private void SpawnWave(double shotTime)
        {
            int remaining = m_TotalProjectileCount - m_SpawnedProjectileCount;
            int countThisWave = System.Math.Min(m_ArmCount, remaining);
            float seedOffset = Context.RandomSeed % 360u;
            float spin = Tuning.RotationSpeed * (float)(shotTime - Context.StartServerTime);

            for (int i = 0; i < countThisWave; i++)
            {
                float armAngle = i * (360f / m_ArmCount);
                Float3 direction = RotateY(new Float3(0f, 0f, 1f), seedOffset + spin + armAngle);
                uint projectileOrdinal = (uint)(++m_SpawnedProjectileCount);
                var request = new BossProjectileSpawnRequest(
                    Tuning.ProjectileId,
                    Services.BossBodyState.EntityId,
                    (Context.CastSequence << 16) | projectileOrdinal,
                    Add(Context.SourcePosition, Float3.Up),
                    direction,
                    Tuning.Damage,
                    Tuning.ProjectileSpeed,
                    Tuning.ProjectileScale,
                    DamageTags.Projectile);
                Services.ProjectileSpawner.TrySpawn(request, out _);
            }
        }
    }
}
