using System;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    public sealed class SpawnLocationQueryAdapter : ISpawnLocationQuery
    {
        private readonly INavigationField navigation;
        private readonly Bounds bounds;
        private readonly float radius;
        private readonly float clearanceRadius;
        private readonly int attempts;
        private readonly int obstacleMask;
        private readonly IRandomSource random;
        private uint sampleIndex;

        public SpawnLocationQueryAdapter(
            INavigationField navigation,
            Bounds bounds,
            float radius = 8f,
            int attempts = 12,
            int obstacleMask = Physics.DefaultRaycastLayers,
            float clearanceRadius = 0.35f,
            IRandomSource random = null)
        {
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.bounds = bounds;
            this.radius = Math.Max(0f, radius);
            this.attempts = Math.Max(1, attempts);
            this.obstacleMask = obstacleMask;
            this.clearanceRadius = Math.Max(0f, clearanceRadius);
            this.random = random;
        }

        public bool IsValid(WorldPosition position)
        {
            Vector3 point = ToVector3(position);
            return bounds.Contains(point) && navigation.IsWalkable(position) &&
                !Physics.CheckSphere(point, clearanceRadius, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        public WorldPosition SampleAround(WorldPosition target)
        {
            if (IsValid(target)) return target;
            for (int i = 0; i < attempts; i++)
            {
                float angle = Next01() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt(Next01()) * radius;
                WorldPosition candidate = new(
                    target.X + Mathf.Cos(angle) * distance,
                    target.Y,
                    target.Z + Mathf.Sin(angle) * distance);
                if (IsValid(candidate)) return candidate;
            }

            // A deterministic ring fallback makes behavior stable when the
            // injected random source is exhausted or all random candidates hit
            // blockers.
            int ringCount = Math.Max(1, attempts);
            for (int i = 0; i < ringCount; i++)
            {
                float angle = (i / (float)ringCount) * Mathf.PI * 2f;
                WorldPosition candidate = new(
                    target.X + Mathf.Cos(angle) * radius,
                    target.Y,
                    target.Z + Mathf.Sin(angle) * radius);
                if (IsValid(candidate)) return candidate;
            }
            return target;
        }

        private float Next01()
        {
            float value = random?.NextFloat() ?? ((sampleIndex++ * 0.61803395f) % 1f);
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
            return Mathf.Clamp01(value);
        }

        private static Vector3 ToVector3(WorldPosition position) =>
            new(position.X, position.Y, position.Z);
    }
}
