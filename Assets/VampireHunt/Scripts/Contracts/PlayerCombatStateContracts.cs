using System;

namespace VampireHunt.Contracts
{
    public enum PlayerCombatState : byte { NonCombat, Combat }
    public enum CombatIntentPolicy : byte { Combat, NonCombat, Support }
    public enum CombatActivityKind : byte { Action, Interaction, Periodic, Support }

    public interface IPlayerCombatStateReader
    {
        PlayerCombatState State { get; }
        bool IsInitialized { get; }
        event Action<PlayerCombatState, PlayerCombatState> StateChanged;
    }
}
