using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>Replicated persistent cue state for enemy debuff VFX and late join.</summary>
public struct NetworkEnemyDebuffState :
    INetworkSerializable,
    IEquatable<NetworkEnemyDebuffState>
{
    public FixedString64Bytes CueTag;
    public ushort References;

    public NetworkEnemyDebuffState(string cueTag, ushort references = 1)
    {
        CueTag = new FixedString64Bytes(cueTag ?? string.Empty);
        References = references;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref CueTag);
        serializer.SerializeValue(ref References);
    }

    public bool Equals(NetworkEnemyDebuffState other) =>
        CueTag.Equals(other.CueTag) && References == other.References;

    public override bool Equals(object obj) =>
        obj is NetworkEnemyDebuffState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(CueTag, References);
}
