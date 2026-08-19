using VampireHunt.Core;
using VampireHunt.Navigation.Domain;

namespace VampireHunt.Navigation.Contracts
{
    /// <summary>
    /// Read-only navigation port used by simulation code. Implementations may
    /// be backed by a flow field, a test grid, or a world adapter; no Unity
    /// object is part of the contract.
    /// </summary>
    public interface INavigationField
    {
        Direction SampleDirection(WorldPosition position, WorldPosition target);

        bool IsWalkable(WorldPosition position);

        WorldPosition TryFindRecovery(WorldPosition position);
    }
}
