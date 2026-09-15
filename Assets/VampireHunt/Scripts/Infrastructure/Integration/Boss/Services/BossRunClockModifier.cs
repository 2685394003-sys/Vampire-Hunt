using UnityEngine;
using VampireHunt.Bootstrap;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class BossRunClockModifier : MonoBehaviour, IRunClockModifier
    {
        [SerializeField] private VampireHuntGameManager runManager;

        public double CurrentDrainRate => runManager != null
            ? runManager.CurrentSnapshot.DrainRate
            : 0d;

        private void Awake()
        {
            ResolveManager();
        }

        public bool TryExtend(double seconds)
        {
            ResolveManager();
            return runManager != null && seconds > 0d && runManager.TryExtendClock(seconds);
        }

        public bool TrySetDrainRate(double drainRate)
        {
            ResolveManager();
            return runManager != null && drainRate >= 0d && runManager.TrySetClockDrainRate(drainRate);
        }

        private void ResolveManager()
        {
            if (runManager == null) runManager = FindAnyObjectByType<VampireHuntGameManager>();
        }
    }
}
