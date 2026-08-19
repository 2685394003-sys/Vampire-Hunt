namespace VampireHunt.Navigation.Domain
{
    /// <summary>Immutable walkability and traversal cost for one cell.</summary>
    public readonly struct FlowCell
    {
        public FlowCell(CellIndex index, bool isWalkable, ushort cost = 1)
        {
            Index = index;
            IsWalkable = isWalkable;
            Cost = isWalkable ? (ushort)(cost == 0 ? 1 : cost) : (ushort)0;
        }

        public CellIndex Index { get; }
        public bool IsWalkable { get; }
        public ushort Cost { get; }

        public static FlowCell Walkable(CellIndex index, ushort cost = 1) =>
            new FlowCell(index, true, cost);

        public static FlowCell Blocked(CellIndex index) =>
            new FlowCell(index, false, 0);
    }
}
