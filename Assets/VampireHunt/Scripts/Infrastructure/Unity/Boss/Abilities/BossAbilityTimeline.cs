using UnityEngine;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    /// <summary>
    /// Maps an authored cue timeline onto a server-selected telegraph duration. Only the
    /// startup section is stretched; resolve/recover offsets keep their authored speed.
    /// </summary>
    public static class BossAbilityTimeline
    {
        public static float RemapTime(
            float authoredTime,
            float authoredTelegraphDuration,
            double effectiveTelegraphDuration)
        {
            float time = Mathf.Max(0f, authoredTime);
            float authoredTelegraph = Mathf.Max(0f, authoredTelegraphDuration);
            float effectiveTelegraph = Mathf.Max(0f, (float)effectiveTelegraphDuration);

            if (authoredTelegraph <= .0001f) return time + effectiveTelegraph;
            if (time <= authoredTelegraph)
                return time * (effectiveTelegraph / authoredTelegraph);
            return time + effectiveTelegraph - authoredTelegraph;
        }

        public static float RemapLifetime(
            float authoredStart,
            float authoredLifetime,
            float authoredTelegraphDuration,
            double effectiveTelegraphDuration)
        {
            if (authoredLifetime <= 0f) return authoredLifetime;
            float start = RemapTime(
                authoredStart, authoredTelegraphDuration, effectiveTelegraphDuration);
            float end = RemapTime(
                authoredStart + authoredLifetime,
                authoredTelegraphDuration,
                effectiveTelegraphDuration);
            return Mathf.Max(.01f, end - start);
        }
    }
}
