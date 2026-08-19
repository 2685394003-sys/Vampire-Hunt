using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Navigation.Domain;

/// <summary>Unity/FlowField adapter for the engine-agnostic navigation port.</summary>
internal sealed class LegacyFlowFieldNavigation : INavigationField
{
    private readonly FlowFieldManager manager;

    public LegacyFlowFieldNavigation(FlowFieldManager value)
    {
        manager = value;
    }

    public Direction SampleDirection(WorldPosition position, WorldPosition target)
    {
        if (manager == null) return Direction.None;
        // The legacy manager owns the active player target lookup. The domain
        // target is still carried through the navigation contract, but this
        // adapter cannot expose a Unity Transform from that pure value.
        Vector3 flow = manager.GetFlowDirection(ToVector3(position));
        if (flow.sqrMagnitude < 0.0001f) return Direction.None;
        return new Direction(Mathf.RoundToInt(flow.x), Mathf.RoundToInt(flow.z));
    }

    public bool IsWalkable(WorldPosition position)
    {
        if (manager == null) return true;
        Vector2Int cell = manager.WorldToGrid(ToVector3(position));
        return manager.IsInGrid(cell.x, cell.y) &&
               manager.GetCellState(cell.x, cell.y) == CellState.Walkable;
    }

    public WorldPosition TryFindRecovery(WorldPosition position)
    {
        if (manager == null || IsWalkable(position)) return position;
        Vector2Int origin = manager.WorldToGrid(ToVector3(position));
        const int maxRadius = 8;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    if (Mathf.Abs(x) != radius && Mathf.Abs(z) != radius) continue;
                    int cellX = origin.x + x;
                    int cellZ = origin.y + z;
                    if (!manager.IsInGrid(cellX, cellZ) ||
                        manager.GetCellState(cellX, cellZ) != CellState.Walkable)
                        continue;
                    Vector3 world = manager.GridToWorld(new Vector2Int(cellX, cellZ));
                    world.y = ToVector3(position).y;
                    return ToWorldPosition(world);
                }
            }
        }
        return position;
    }

    private static Vector3 ToVector3(WorldPosition position) =>
        new Vector3(position.X, position.Y, position.Z);

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);
}
