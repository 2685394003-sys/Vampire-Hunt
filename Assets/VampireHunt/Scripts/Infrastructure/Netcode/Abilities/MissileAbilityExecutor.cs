using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-side executor for the missile bomb weapon. Spawns a scattered volley of parabola
    /// bombs (5-8), each landing near the aim point; each bomb's speed is recomputed for its own
    /// landing distance so the cluster follows the mouse and spreads around it.
    /// </summary>
    public sealed class MissileAbilityExecutor : MonoBehaviour, ICombatAbilityNetworkExecutor
    {
        [SerializeField] private uint abilityId = 130;
        [SerializeField] private NetworkObject bombPrefab;

        [Header("Volley")]
        [SerializeField] private int minCount = 5;
        [SerializeField] private int maxCount = 8;
        [Tooltip("Horizontal scatter angle (degrees) around the aim direction.")]
        [SerializeField] private float spreadAngle = 12f;
        [Tooltip("Landing-distance jitter (meters) around the aim landing point.")]
        [SerializeField] private float distanceSpread = 1.5f;
        [Tooltip("Gravity used to recompute each bomb's launch speed (must match the bomb prefab).")]
        [SerializeField] private float gravity = 9.8f;

        [Header("Audio (Wwise)")]
        [Tooltip("Wwise 事件名（发射音），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string fireEventName = "";

        public uint AbilityId => abilityId;

        public bool ExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message)
        {
            if (manager == null || !manager.IsServer || bombPrefab == null ||
                message.AbilityId != abilityId) return false;

            Vector3 baseDirection = message.Direction.sqrMagnitude > 0.0001f
                ? message.Direction.normalized
                : transform.forward;

            float baseDistance = message.TravelDistance;
            float arcAngle = message.SpreadAngle;   // repurposed: launch elevation (degrees)
            float theta = Mathf.Clamp(arcAngle, 1f, 89f) * Mathf.Deg2Rad;
            float sin2 = Mathf.Max(0.1f, Mathf.Sin(2f * theta));

            int count = Random.Range(minCount, maxCount + 1);

            if (!string.IsNullOrEmpty(fireEventName)) WwiseAudioBridge.PostEvent(fireEventName, gameObject);

            for (int i = 0; i < count; i++)
            {
                float yaw = Random.Range(-spreadAngle, spreadAngle);
                Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * baseDirection;
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

                float distance = Mathf.Max(1f, baseDistance + Random.Range(-distanceSpread, distanceSpread));
                float speed = Mathf.Sqrt(distance * gravity / sin2);

                var perBombMessage = message;
                perBombMessage.Direction = direction;
                perBombMessage.TravelDistance = distance;
                perBombMessage.ProjectileSpeed = speed;

                NetworkObject bomb = Instantiate(bombPrefab, message.Origin, rotation);
                if (bomb.TryGetComponent<ParabolaBombProjectile>(out var projectile))
                {
                    projectile.ConfigureServer(senderClientId, perBombMessage);
                }
                bomb.SpawnWithOwnership(senderClientId);
            }

            return true;
        }
    }
}
