using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>Compact replicated summary used by UI, reconnection and late join.</summary>
public struct NetworkBloodPactState : INetworkSerializable, IEquatable<NetworkBloodPactState>
{
    public FixedString64Bytes PactId;
    public ushort Stacks;

    public NetworkBloodPactState(string pactId, ushort stacks = 1)
    {
        PactId = new FixedString64Bytes(pactId ?? string.Empty);
        Stacks = stacks;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref PactId);
        serializer.SerializeValue(ref Stacks);
    }

    public bool Equals(NetworkBloodPactState other) =>
        PactId.Equals(other.PactId) && Stacks == other.Stacks;

    public override bool Equals(object obj) =>
        obj is NetworkBloodPactState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(PactId, Stacks);
}
