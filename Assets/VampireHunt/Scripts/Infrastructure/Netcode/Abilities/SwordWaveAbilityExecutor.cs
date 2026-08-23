using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Server-side executor for ability id 1. Additional abilities get sibling executors.</summary>
    [DisallowMultipleComponent]
    public sealed class SwordWaveAbilityExecutor : MonoBehaviour, ICombatAbilityNetworkExecutor
    {
        private const uint SwordWaveAbilityId = 1;
        [SerializeField] private NetworkObject swordWavePrefab;

        public uint AbilityId => SwordWaveAbilityId;

        public bool ExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message)
        {
            if (manager == null || !manager.IsServer || swordWavePrefab == null ||
                message.AbilityId != SwordWaveAbilityId) return false;

            Vector3 baseDirection = message.Direction.sqrMagnitude > 0.0001f
                ? message.Direction.normalized
                : transform.forward;
            int projectileCount = Mathf.Clamp(message.ProjectileCount, 1, 32);
            GameObject ownerObject = gameObject;
            if (manager.ConnectedClients.TryGetValue(senderClientId, out var client) && client.PlayerObject != null)
            {
                ownerObject = client.PlayerObject.gameObject;
            }

            for (int i = 0; i < projectileCount; i++)
            {
                float yaw = projectileCount == 1
                    ? 0f
                    : Mathf.Lerp(-message.SpreadAngle * 0.5f, message.SpreadAngle * 0.5f,
                        i / (float)(projectileCount - 1));
                Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * baseDirection;
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
                NetworkObject projectileObject = Instantiate(swordWavePrefab, message.Origin, rotation);

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
