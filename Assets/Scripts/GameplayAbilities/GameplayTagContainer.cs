using System;
using System.Collections.Generic;

/// <summary>
/// Reference-counted hierarchical tags. Adding State.Buff.Berserker also grants
/// State and State.Buff, so tag queries stay O(1) regardless of tree depth.
/// </summary>
public sealed class GameplayTagContainer
{
    private readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);

    public int Count => counts.Count;

    public bool Add(string tag)
    {
        tag = Normalize(tag);
        if (tag == null) return false;

        AddCount(tag);
        for (int index = tag.LastIndexOf('.'); index > 0; index = tag.LastIndexOf('.', index - 1))
            AddCount(tag.Substring(0, index));
        return true;
    }

    public bool Remove(string tag)
    {
        tag = Normalize(tag);
        if (tag == null || !counts.ContainsKey(tag)) return false;

        RemoveCount(tag);
        for (int index = tag.LastIndexOf('.'); index > 0; index = tag.LastIndexOf('.', index - 1))
            RemoveCount(tag.Substring(0, index));
        return true;
    }

    public bool Has(string tag)
    {
        tag = Normalize(tag);
        return tag != null && counts.ContainsKey(tag);
    }

    public bool HasAll(IReadOnlyList<string> tags)
    {
        if (tags == null) return true;
        for (int index = 0; index < tags.Count; index++)
            if (!Has(tags[index])) return false;
        return true;
    }

    public bool HasAny(IReadOnlyList<string> tags)
    {
        if (tags == null) return false;
        for (int index = 0; index < tags.Count; index++)
            if (Has(tags[index])) return true;
        return false;
    }

    public void Clear() => counts.Clear();

    private void AddCount(string tag)
    {
        counts.TryGetValue(tag, out int count);
        counts[tag] = count + 1;
    }

    private void RemoveCount(string tag)
    {
        if (!counts.TryGetValue(tag, out int count)) return;
        if (count <= 1) counts.Remove(tag);
        else counts[tag] = count - 1;
    }

    private static string Normalize(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        tag = tag.Trim().Trim('.');
        return tag.Length > 0 ? tag : null;
    }
}
