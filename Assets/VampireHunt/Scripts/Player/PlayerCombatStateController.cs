using System;
using System.Collections.Generic;
using VampireHunt.Contracts;

namespace VampireHunt.Player
{
    /// <summary>Server simulation model. No scene queries, health sampling or Unity clock.</summary>
    public sealed class PlayerCombatStateController
    {
        private readonly HashSet<ulong> m_Sustained = new HashSet<ulong>();
        public PlayerCombatState State { get; private set; }
        public bool IsAlive { get; private set; }
        public uint Generation { get; private set; }
        public double ExitAt { get; private set; }
        public double Delay { get; }
        public bool HasSustainedActions => m_Sustained.Count != 0;

        public PlayerCombatStateController(double delay)
        {
            if (double.IsNaN(delay) || double.IsInfinity(delay) || delay < 0)
                throw new ArgumentOutOfRangeException(nameof(delay));
            Delay = delay;
        }

        public void Reset(bool alive)
        {
            Generation++;
            IsAlive = alive;
            State = PlayerCombatState.NonCombat;
            ExitAt = double.PositiveInfinity;
            m_Sustained.Clear();
        }

        public void Record(double now)
        {
            if (!IsAlive) return;
            State = PlayerCombatState.Combat;
            ExitAt = now + Delay;
        }

        public bool Begin(ulong handle, uint generation, double now)
        {
            if (!IsAlive || generation != Generation || !m_Sustained.Add(handle)) return false;
            Record(now);
            return true;
        }

        public bool End(ulong handle, uint generation, double now)
        {
            if (!IsAlive || generation != Generation || !m_Sustained.Remove(handle)) return false;
            Record(now);
            return true;
        }

        public void Tick(double now)
        {
            if (IsAlive && !HasSustainedActions && State == PlayerCombatState.Combat && now >= ExitAt)
                State = PlayerCombatState.NonCombat;
        }
    }
}
