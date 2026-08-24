using System;
using VampireHunt.Contracts;

namespace VampireHunt.Enemies
{
    public enum EnemyStat : byte
    {
        MaxHealth = 0,
        MoveSpeed = 1,
        DetectionRange = 2,
        AttackRange = 3,
        AttackDamage = 4,
        AttackKnockback = 5,
        SpawnDuration = 6,
        TelegraphDuration = 7,
        ActiveDuration = 8,
        RecoveryDuration = 9,
        ScarletReward = 10,
        Count = 11
    }

    public readonly struct EnemyStatModifierDefinition
    {
        public EnemyStat Stat { get; }
        public AttributeModifierOperation Operation { get; }
        public float ConstantValue { get; }
        public float ValuePerStack { get; }

        public EnemyStatModifierDefinition(
            EnemyStat stat,
            AttributeModifierOperation operation,
            float constantValue,
            float valuePerStack)
        {
            Stat = stat;
            Operation = operation;
            ConstantValue = IsFinite(constantValue) ? constantValue : 0f;
            ValuePerStack = IsFinite(valuePerStack) ? valuePerStack : 0f;
        }

        public float ResolveValue(int stacks) =>
            ConstantValue + ValuePerStack * Math.Max(0, stacks);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Immutable, instance-local enemy values resolved when the enemy is spawned.</summary>
    public sealed class EnemyRuntimeStats
    {
        public float MaxHealth { get; }
        public float MoveSpeed { get; }
        public float DetectionRange { get; }
        public float AttackRange { get; }
        public float AttackBreakRange => AttackRange * 1.5f;
        public float AttackDamage { get; }
        public float AttackKnockback { get; }
        public double SpawnDuration { get; }
        public double TelegraphDuration { get; }
        public double ActiveDuration { get; }
        public double RecoveryDuration { get; }
        public float ScarletReward { get; }

        internal EnemyRuntimeStats(
            float maxHealth,
            float moveSpeed,
            float detectionRange,
            float attackRange,
            float attackDamage,
            float attackKnockback,
            double spawnDuration,
            double telegraphDuration,
            double activeDuration,
            double recoveryDuration,
            float scarletReward)
        {
            MaxHealth = Math.Max(1f, maxHealth);
            MoveSpeed = Math.Max(0f, moveSpeed);
            AttackRange = Math.Max(0.1f, attackRange);
            DetectionRange = Math.Max(AttackRange, detectionRange);
            AttackDamage = Math.Max(0f, attackDamage);
            AttackKnockback = Math.Max(0f, attackKnockback);
            SpawnDuration = Math.Max(0d, spawnDuration);
            TelegraphDuration = Math.Max(0d, telegraphDuration);
            ActiveDuration = Math.Max(0.01d, activeDuration);
            RecoveryDuration = Math.Max(0d, recoveryDuration);
            ScarletReward = Math.Max(0f, scarletReward);
        }

        public static EnemyRuntimeStats FromArchetype(EnemyArchetypeDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return new EnemyRuntimeStats(
                definition.MaxHealth,
                definition.MoveSpeed,
                definition.DetectionRange,
                definition.AttackRange,
                definition.AttackDamage,
                definition.AttackKnockback,
                definition.SpawnDuration,
                definition.TelegraphDuration,
                definition.ActiveDuration,
                definition.RecoveryDuration,
                definition.ScarletReward);
        }
    }

    /// <summary>Deterministically composes all spawn-time affix modifiers.</summary>
    public sealed class EnemyRuntimeStatsBuilder
    {
        private readonly EnemyArchetypeDefinition m_Base;
        private readonly float[] m_Flat = new float[(int)EnemyStat.Count];
        private readonly float[] m_AdditivePercent = new float[(int)EnemyStat.Count];
        private readonly float[] m_Multiplicative = new float[(int)EnemyStat.Count];

        public EnemyRuntimeStatsBuilder(EnemyArchetypeDefinition definition)
        {
            m_Base = definition ?? throw new ArgumentNullException(nameof(definition));
            for (int i = 0; i < m_Multiplicative.Length; i++) m_Multiplicative[i] = 1f;
        }

        public void Add(in EnemyStatModifierDefinition modifier, int stacks)
        {
            int index = (int)modifier.Stat;
            if (index < 0 || index >= (int)EnemyStat.Count || stacks <= 0) return;
            float value = modifier.ResolveValue(stacks);
            if (float.IsNaN(value) || float.IsInfinity(value)) return;

            switch (modifier.Operation)
            {
                case AttributeModifierOperation.Flat:
                    m_Flat[index] += value;
                    break;
                case AttributeModifierOperation.AdditivePercent:
                    m_AdditivePercent[index] += value;
                    break;
                case AttributeModifierOperation.Multiplicative:
                    m_Multiplicative[index] *= Math.Max(0f, 1f + value);
                    break;
            }
        }

        public EnemyRuntimeStats Build()
        {
            return new EnemyRuntimeStats(
                Resolve(EnemyStat.MaxHealth, m_Base.MaxHealth),
                Resolve(EnemyStat.MoveSpeed, m_Base.MoveSpeed),
                Resolve(EnemyStat.DetectionRange, m_Base.DetectionRange),
                Resolve(EnemyStat.AttackRange, m_Base.AttackRange),
                Resolve(EnemyStat.AttackDamage, m_Base.AttackDamage),
                Resolve(EnemyStat.AttackKnockback, m_Base.AttackKnockback),
                Resolve(EnemyStat.SpawnDuration, (float)m_Base.SpawnDuration),
                Resolve(EnemyStat.TelegraphDuration, (float)m_Base.TelegraphDuration),
                Resolve(EnemyStat.ActiveDuration, (float)m_Base.ActiveDuration),
                Resolve(EnemyStat.RecoveryDuration, (float)m_Base.RecoveryDuration),
                Resolve(EnemyStat.ScarletReward, m_Base.ScarletReward));
        }

        private float Resolve(EnemyStat stat, float baseValue)
        {
            int index = (int)stat;
            return (baseValue + m_Flat[index]) *
                   Math.Max(0f, 1f + m_AdditivePercent[index]) *
                   m_Multiplicative[index];
        }
    }
}
