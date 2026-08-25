using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossRadialVolleyAbilityLogic : BossGameplayAbilityLogic
    {
        protected override void Resolve(double serverTime)
        {
            if (Services.ProjectileSpawner == null || Services.BossBodyState == null) return;
            float seedOffset = Context.RandomSeed % 360u;
            float spin = Tuning.RotationSpeed * (float)(serverTime - Context.StartServerTime);
            for (int i = 0; i < Tuning.ProjectileCount; i++)
            {
                Float3 direction = RotateY(new Float3(0f, 0f, 1f),
                    seedOffset + spin + 360f * i / Tuning.ProjectileCount);
                var request = new BossProjectileSpawnRequest(
                    Tuning.ProjectileId,
                    Services.BossBodyState.EntityId,
                    (Context.CastSequence << 16) | (uint)(i + 1),
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
