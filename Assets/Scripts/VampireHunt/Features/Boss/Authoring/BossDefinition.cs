using System;
using VampireHunt.Boss.Contracts;
using UnityEngine;

namespace VampireHunt.Boss.Authoring
{
    [Serializable]
    public sealed class BossPhaseDefinition
    {
        [SerializeField] private BossPhase phase = BossPhase.PhaseOne;
        [SerializeField, Range(0.001f, 1f)] private float enterAtHealthRatio = 1f;

        public BossPhase Phase => phase;
        public float EnterAtHealthRatio => enterAtHealthRatio;
    }

    [CreateAssetMenu(menuName = "Vampire Hunt/Boss/Attack Definition", fileName = "BossAttackDefinition")]
    public sealed class BossAttackDefinition : ScriptableObject
    {
        [SerializeField] private BossAttackId id = BossAttackId.GuardSweep;
        [SerializeField, Min(0f)] private float cooldown = 3f;
        [SerializeField, Min(0f)] private float weight = 1f;
        [SerializeField, Min(0)] private int damage = 1;
        [SerializeField] private BossPhase minimumPhase = BossPhase.PhaseOne;
        [SerializeField, Min(0f)] private float telegraphSeconds = 0.8f;
        [SerializeField, Min(0f)] private float activeSeconds = 0.2f;
        [SerializeField, Min(0f)] private float range = 6f;
        [SerializeField, Min(0f)] private float width = 1.5f;
        [SerializeField, Min(0f)] private float knockback;
        [SerializeField, Min(0)] private int projectileCount;
        [SerializeField, Min(0f)] private float projectileSpeed;
        [SerializeField] private string presentationCue = string.Empty;

        public BossAttackId Id => id;
        public float Cooldown => cooldown;
        public float Weight => weight;
        public int Damage => damage;
        public BossPhase MinimumPhase => minimumPhase;
        public float TelegraphSeconds => telegraphSeconds;
        public float ActiveSeconds => activeSeconds;
        public float Range => range;
        public float Width => width;
        public float Knockback => knockback;
        public int ProjectileCount => projectileCount;
        public float ProjectileSpeed => projectileSpeed;
        public PresentationCueId Cue => new(presentationCue);

        private void OnValidate()
        {
            cooldown = Mathf.Max(0f, cooldown);
            weight = Mathf.Max(0f, weight);
            damage = Mathf.Max(0, damage);
            telegraphSeconds = Mathf.Max(0f, telegraphSeconds);
            activeSeconds = Mathf.Max(0f, activeSeconds);
            range = Mathf.Max(0f, range);
            width = Mathf.Max(0f, width);
            knockback = Mathf.Max(0f, knockback);
            projectileCount = Mathf.Max(0, projectileCount);
            projectileSpeed = Mathf.Max(0f, projectileSpeed);
        }
    }

    [CreateAssetMenu(menuName = "Vampire Hunt/Boss/Boss Definition", fileName = "BossDefinition")]
    public sealed class BossDefinition : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField] private BossPhaseDefinition[] phases = Array.Empty<BossPhaseDefinition>();
        [SerializeField] private BossAttackDefinition[] attacks = Array.Empty<BossAttackDefinition>();
        [SerializeField, Min(0)] private int guardIntegrity = 50;
        [SerializeField, Range(0f, 1f)] private float guardDamageReduction = 0.5f;
        [SerializeField, Min(0.01f)] private float staggerSeconds = 4f;
        [SerializeField, Range(0.01f, 0.99f)] private float contractTriggerHealthRatio = 0.2f;
        [SerializeField, Min(0f)] private float contractSeconds = 30f;
        [SerializeField, Min(0.01f)] private float contractCountdownRate = 2f;

        public int MaxHealth => maxHealth;
        public BossPhaseDefinition[] Phases => phases ?? Array.Empty<BossPhaseDefinition>();
        public BossAttackDefinition[] Attacks => attacks ?? Array.Empty<BossAttackDefinition>();
        public int GuardIntegrity => guardIntegrity;
        public float GuardDamageReduction => guardDamageReduction;
        public float StaggerSeconds => staggerSeconds;
        public float ContractTriggerHealthRatio => contractTriggerHealthRatio;
        public float ContractSeconds => contractSeconds;
        public float ContractCountdownRate => contractCountdownRate;

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            guardIntegrity = Mathf.Max(0, guardIntegrity);
            guardDamageReduction = Mathf.Clamp01(guardDamageReduction);
            staggerSeconds = Mathf.Max(0.01f, staggerSeconds);
            contractTriggerHealthRatio = Mathf.Clamp(contractTriggerHealthRatio, 0.01f, 0.99f);
            contractSeconds = Mathf.Max(0f, contractSeconds);
            contractCountdownRate = Mathf.Max(0.01f, contractCountdownRate);
            phases ??= Array.Empty<BossPhaseDefinition>();
            attacks ??= Array.Empty<BossAttackDefinition>();
        }
    }
}
