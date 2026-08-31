using Blocks.Gameplay.Core;
using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-side executor for projectile weapons (sniper, auto-rifle, ...).
    /// One instance per weapon: set <see cref="abilityId"/> and <see cref="projectilePrefab"/> in the
    /// inspector. Multiple instances are allowed so several projectile weapons can coexist.
    /// In single-player/host mode this runs on the local server path unchanged.
    /// </summary>
    public sealed class ProjectileWeaponAbilityExecutor : MonoBehaviour, ICombatAbilityNetworkExecutor
    {
        [SerializeField] private uint abilityId = 110;
        [SerializeField] private NetworkObject projectilePrefab;
        [Tooltip("Wwise 事件名（发射音），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string fireEventName = "";

        public uint AbilityId => abilityId;

        public bool ExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message)
        {
            if (manager == null || !manager.IsServer || projectilePrefab == null ||
                message.AbilityId != abilityId) return false;

            Vector3 baseDirection = message.Direction.sqrMagnitude > 0.0001f
                ? message.Direction.normalized
                : transform.forward;
            int projectileCount = Mathf.Clamp(message.ProjectileCount, 1, 32);
            GameObject ownerObject = gameObject;
            if (manager.ConnectedClients.TryGetValue(senderClientId, out var client) && client.PlayerObject != null)
            {
                ownerObject = client.PlayerObject.gameObject;
            }

            if (!string.IsNullOrEmpty(fireEventName)) WwiseAudioBridge.PostEvent(fireEventName, gameObject);

            for (int i = 0; i < projectileCount; i++)
            {
                float yaw;
                if (projectileCount == 1)
                {
                    // Single projectile: random spread within ±SpreadAngle/2 (accuracy loss for auto-rifle).
                    yaw = message.SpreadAngle > 0.0001f
                        ? (Random.value - 0.5f) * message.SpreadAngle
                        : 0f;
                }
                else
                {
                    yaw = Mathf.Lerp(-message.SpreadAngle * 0.5f, message.SpreadAngle * 0.5f,
                        i / (float)(projectileCount - 1));
                }
                Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * baseDirection;
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
                NetworkObject projectileObject = Instantiate(projectilePrefab, message.Origin, rotation);

                // All projectile weapons reuse SwordWaveEffect's trusted-hit combat-port adapter.
                // (The name is legacy; it is a generic trusted-hit detector for player projectiles.)
                if (projectileObject.TryGetComponent<SwordWaveEffect>(out var effect))
                {
                    effect.ConfigureServer(senderClientId, message);
                }

                if (projectileObject.TryGetComponent<ModularProjectile>(out var projectile))
                {
                    var context = new ShootingContext
                    {
                        owner = ownerObject,
                        origin = message.Origin,
                        direction = direction,
                        damage = message.Damage,
                        ownerClientId = senderClientId
                    };
                    projectile.LaunchWithContext(message.Origin, direction, Vector3.zero, null, context, ownerObject);
                }

                projectileObject.SpawnWithOwnership(senderClientId);
            }

            return true;
        }
    }
}
