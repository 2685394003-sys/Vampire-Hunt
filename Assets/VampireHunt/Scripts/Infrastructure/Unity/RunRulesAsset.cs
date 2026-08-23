using UnityEngine;
using VampireHunt.Run;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "VH_RunRules", menuName = "Vampire Hunt/Run Rules")]
    public sealed class RunRulesAsset : ScriptableObject
    {
        [Header("Run Start")]
        [SerializeField, Min(1f)] private float initialDurationSeconds = 300f;
        [SerializeField, Min(1)] private int minimumPlayersToStart = 1;

        [Header("Replication")]
        [SerializeField, Min(0.1f)] private float snapshotHeartbeatSeconds = 1f;

        public int MinimumPlayersToStart => Mathf.Max(1, minimumPlayersToStart);
        public float SnapshotHeartbeatSeconds => Mathf.Max(0.1f, snapshotHeartbeatSeconds);

        public RunRules CreateRules()
        {
            return new RunRules(
                Mathf.Max(1f, initialDurationSeconds),
                MinimumPlayersToStart);
        }

        private void OnValidate()
        {
            initialDurationSeconds = Mathf.Max(1f, initialDurationSeconds);
            minimumPlayersToStart = Mathf.Max(1, minimumPlayersToStart);
            snapshotHeartbeatSeconds = Mathf.Max(0.1f, snapshotHeartbeatSeconds);
        }
    }
}
