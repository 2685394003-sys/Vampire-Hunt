using System;
using System.Collections.Generic;

namespace VampireHunt.Abilities.Domain
{
    /// <summary>
    /// Runtime-owned hierarchical gameplay tags. A leaf such as
    /// <c>State.Control.Frozen</c> contributes reference-counted entries for
    /// itself, <c>State.Control</c> and <c>State</c>. The type has no Unity or
    /// networking dependency; presenters receive cues/read models instead.
    /// </summary>
    public sealed class GameplayTagSet
    {
        private readonly Dictionary<string, int> counts =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count => counts.Count;

        public bool Add(string tag)
        {
            string normalized = Normalize(tag);
            if (normalized == null) return false;

            AddCount(normalized);
            for (int index = normalized.LastIndexOf('.'); index > 0;
                 index = normalized.LastIndexOf('.', index - 1))
            {
                AddCount(normalized.Substring(0, index));
            }
            return true;
        }

        public bool Remove(string tag)
        {
            string normalized = Normalize(tag);
            if (normalized == null || !counts.ContainsKey(normalized)) return false;

            RemoveCount(normalized);
            for (int index = normalized.LastIndexOf('.'); index > 0;
                 index = normalized.LastIndexOf('.', index - 1))
            {
                RemoveCount(normalized.Substring(0, index));
            }
            return true;
        }

        public bool Has(string tag)
        {
            string normalized = Normalize(tag);
            return normalized != null && counts.ContainsKey(normalized);
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
            string normalized = tag.Trim().Trim('.');
            return normalized.Length == 0 ? null : normalized;
        }
    }
}
