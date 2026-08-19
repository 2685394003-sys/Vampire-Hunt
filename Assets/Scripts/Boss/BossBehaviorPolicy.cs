using UnityEngine;

internal enum BossMovementIntent
{
    Hold = 0,
    Approach = 1,
    Retreat = 2
}

internal readonly struct BossBehaviorDecision
{
    public BossMovementIntent Movement { get; }
    public BossState State { get; }
    public Vector3 Direction { get; }
    public bool AllowAttack { get; }

    public BossBehaviorDecision(BossMovementIntent movement, BossState state, Vector3 direction, bool allowAttack)
    {
        Movement = movement;
        State = state;
        Direction = direction;
        AllowAttack = allowAttack;
    }
}

/// <summary>
/// Compatibility shell retained for old serialized/debug callers. Attack
/// selection and phase transitions now live in BossAttackSelector and
/// BossPhaseStateMachine; this shim never decides damage, phase or attacks.
/// </summary>
[System.Obsolete("BossBehaviorPolicy is a compatibility adapter; use BossRuntime.")]
internal sealed class BossBehaviorPolicy
{
    public BossBehaviorPolicy(BossConfig bossConfig) { }

    public BossBehaviorDecision Evaluate(
        BossEncounterMode encounterMode,
        bool isVisible,
        float targetDistance,
        Vector3 directionToTarget,
        int phase) => new(BossMovementIntent.Hold,
            encounterMode == BossEncounterMode.Battle ? BossState.BattleIdle : BossState.OffscreenIdle,
            Vector3.zero,
            true);

    public void Reset() { }
}
