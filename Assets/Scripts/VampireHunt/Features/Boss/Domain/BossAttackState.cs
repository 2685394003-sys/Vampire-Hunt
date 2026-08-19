using System;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;

namespace VampireHunt.Boss.Domain
{
    public sealed class AttackCooldownSet
    {
        private readonly Dictionary<BossAttackId, float> remaining = new();

        public float GetRemaining(BossAttackId attackId) =>
            remaining.TryGetValue(attackId, out float value) ? value : 0f;

        public bool IsReady(BossAttackId attackId) => GetRemaining(attackId) <= 0f;

        internal void Start(BossAttackId attackId, float cooldown) =>
            remaining[attackId] = Math.Max(0f, cooldown);

        internal void Tick(float deltaTime)
        {
            if (deltaTime <= 0f || remaining.Count == 0) return;
            BossAttackId[] keys = new BossAttackId[remaining.Count];
            remaining.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
                remaining[keys[i]] = Math.Max(0f, remaining[keys[i]] - deltaTime);
        }

        internal void Reset() => remaining.Clear();
    }

    internal sealed class BossAttackState
    {
        public BossAttackId CurrentAttack { get; private set; }
        public bool IsExecuting { get; private set; }
        public AttackCooldownSet Cooldowns { get; } = new();
        public AttackPlan CurrentPlan { get; private set; }
        public float Elapsed { get; private set; }

        public bool TryBegin(AttackPlan plan, float cooldown)
        {
            if (IsExecuting || !Cooldowns.IsReady(plan.AttackId))
                return false;
            CurrentAttack = plan.AttackId;
            CurrentPlan = plan;
            Elapsed = 0f;
            IsExecuting = true;
            Cooldowns.Start(plan.AttackId, cooldown);
            return true;
        }

        public bool Tick(float deltaTime)
        {
            float safeDelta = Math.Max(0f, deltaTime);
            Cooldowns.Tick(safeDelta);
            if (!IsExecuting) return false;
            Elapsed += safeDelta;
            if (Elapsed < CurrentPlan.Duration) return false;
            Complete();
            return true;
        }

        public void Complete()
        {
            IsExecuting = false;
            CurrentAttack = BossAttackId.None;
            CurrentPlan = default;
            Elapsed = 0f;
        }

        public void Cancel() => Complete();

        public void Reset()
        {
            Complete();
            Cooldowns.Reset();
        }
    }
}
