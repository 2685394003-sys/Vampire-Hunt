using System;
using System.Collections.Generic;
using VampireHunt.Abilities.Domain;

/// <summary>
/// Serialization/source-compatibility facade for the Abilities domain tag set.
/// Adding State.Buff.Berserker also grants State and State.Buff, so tag queries
/// stay O(1) regardless of tree depth.
/// </summary>
[Obsolete("Use VampireHunt.Abilities.Domain.GameplayTagSet from the ability runtime.")]
public sealed class GameplayTagContainer
{
    private readonly GameplayTagSet tags = new();

    public int Count => tags.Count;

    public bool Add(string tag) => tags.Add(tag);

    public bool Remove(string tag) => tags.Remove(tag);

    public bool Has(string tag) => tags.Has(tag);

    public bool HasAll(IReadOnlyList<string> requiredTags) => tags.HasAll(requiredTags);

    public bool HasAny(IReadOnlyList<string> anyTags) => tags.HasAny(anyTags);

    public void Clear() => tags.Clear();
}
