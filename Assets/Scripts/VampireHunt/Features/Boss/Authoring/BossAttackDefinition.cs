using VampireHunt.Boss.Contracts;
using UnityEngine;

namespace VampireHunt.Boss.Authoring
{
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
}
