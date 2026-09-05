namespace VampireHunt.Contracts
{
    /// <summary>敌人网络状态投影出的只读表现模型，不暴露 NGO 或玩法写入接口。</summary>
    public readonly struct EnemyPresentationState
    {
        public ulong EntityId { get; }
        public byte State { get; }
        public float CurrentHealth { get; }
        public float MaxHealth { get; }
        public double StateEndServerTime { get; }
        public uint AttackSequence { get; }
        public Float3 AttackAimDirection { get; }
        public uint Revision { get; }

        public EnemyPresentationState(
            ulong entityId,
            byte state,
            float currentHealth,
            float maxHealth,
            double stateEndServerTime,
            uint attackSequence,
            in Float3 attackAimDirection,
            uint revision)
        {
            EntityId = entityId;
            State = state;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            StateEndServerTime = stateEndServerTime;
            AttackSequence = attackSequence;
            AttackAimDirection = attackAimDirection;
            Revision = revision;
        }
    }

    public interface IEnemyPresentationSink
    {
        void Apply(in EnemyPresentationState state);
    }
}
