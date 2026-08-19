using System;
using System.Collections.Generic;
using VampireHunt.Boss.Domain;

namespace VampireHunt.Boss.Authoring
{
    public static class BossSpecFactory
    {
        public static BossSpec Create(BossDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            List<BossPhaseSpec> phases = new(definition.Phases.Length);
            for (int i = 0; i < definition.Phases.Length; i++)
            {
                BossPhaseDefinition phase = definition.Phases[i]
                    ?? throw new InvalidOperationException($"Boss phase at index {i} is null.");
                phases.Add(new BossPhaseSpec(phase.Phase, phase.EnterAtHealthRatio));
            }

            List<BossAttackSpec> attacks = new(definition.Attacks.Length);
            for (int i = 0; i < definition.Attacks.Length; i++)
            {
                BossAttackDefinition attack = definition.Attacks[i]
                    ?? throw new InvalidOperationException($"Boss attack at index {i} is null.");
                attacks.Add(new BossAttackSpec(
                    attack.Id,
                    attack.Cooldown,
                    attack.Weight,
                    attack.Damage,
                    attack.MinimumPhase,
                    attack.TelegraphSeconds,
                    attack.ActiveSeconds,
                    attack.Range,
                    attack.Width,
                    attack.Knockback,
                    attack.ProjectileCount,
                    attack.ProjectileSpeed,
                    attack.Cue));
            }

            return new BossSpec(
                definition.MaxHealth,
                new PhaseSpecSet(phases),
                new BossAttackSpecSet(attacks),
                definition.GuardIntegrity,
                definition.GuardDamageReduction,
                definition.StaggerSeconds,
                definition.ContractTriggerHealthRatio,
                definition.ContractSeconds,
                definition.ContractCountdownRate);
        }
    }
}
