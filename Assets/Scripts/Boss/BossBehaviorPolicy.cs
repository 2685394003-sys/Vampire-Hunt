using UnityEngine;

internal enum BossMovementIntent
{
    Hold,
    Approach,
    Retreat
}

internal readonly struct BossBehaviorDecision
{
    public BossMovementIntent Movement { get; }
    public BossState State { get; }
    public Vector3 Direction { get; }
    public bool AllowAttack { get; }

    public BossBehaviorDecision(
        BossMovementIntent movement,
        BossState state,
        Vector3 direction,
        bool allowAttack)
    {
        Movement = movement;
        State = state;
        Direction = direction;
        AllowAttack = allowAttack;
    }
}

/// <summary>
/// Stateless inputs and a tiny retained hysteresis flag are kept outside the
/// MonoBehaviour coordinator. This makes movement policy deterministic and
/// prevents presentation or attack code from owning encounter rules.
/// </summary>
internal sealed class BossBehaviorPolicy
{
    private readonly BossConfig config;
    private bool retreatLatched;

    public BossBehaviorPolicy(BossConfig bossConfig)
    {
        config = bossConfig;
    }

    public BossBehaviorDecision Evaluate(
        BossEncounterMode encounterMode,
        bool isVisible,
        float targetDistance,
        Vector3 directionToTarget,
        int phase)
    {
        if (config == null || directionToTarget.sqrMagnitude < 0.001f)
        {
            return Hold();
        }

        if (encounterMode == BossEncounterMode.Hunt)
        {
            return EvaluateHunt(isVisible, targetDistance, directionToTarget);
        }

        retreatLatched = false;
        bool mustHold = targetDistance <= config.stoppingDistance ||
                        (config.stationaryAfterFirstPhase && phase >= 1);
        return mustHold
            ? new BossBehaviorDecision(
                BossMovementIntent.Hold,
                BossState.BattleIdle,
                Vector3.zero,
                true)
            : new BossBehaviorDecision(
                BossMovementIntent.Approach,
                BossState.Chase,
                directionToTarget.normalized,
                true);
    }

    public void Reset()
    {
        retreatLatched = false;
    }

    private BossBehaviorDecision EvaluateHunt(
        bool isVisible,
        float targetDistance,
        Vector3 directionToTarget)
    {
        if (!isVisible)
        {
            retreatLatched = false;
            return Hold();
        }

        if (targetDistance <= config.huntRetreatStartDistance)
        {
            retreatLatched = true;
        }
        else if (targetDistance >= config.huntRetreatStopDistance)
        {
            retreatLatched = false;
        }

        if (!retreatLatched)
        {
            return Hold();
        }

        return new BossBehaviorDecision(
            BossMovementIntent.Retreat,
            BossState.Retreat,
            -directionToTarget.normalized,
            false);
    }

    private static BossBehaviorDecision Hold()
    {
        return new BossBehaviorDecision(
            BossMovementIntent.Hold,
            BossState.OffscreenIdle,
            Vector3.zero,
            true);
    }
}
