using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class EnemyProjectileAttackExecutor : MonoBehaviour, IEnemyAttackExecutor
    {
        [SerializeField] private NetworkObject projectilePrefab;
        [SerializeField] private Transform muzzle;
        [Min(0.1f)] [SerializeField] private float projectileSpeed = 11f;
        [SerializeField] private LayerMask impactMask = (1 << 0) | (1 << 3) | (1 << 6);
        [SerializeField] private bool useShooterPool = true;

        public bool TryExecute(in EnemyAttackExecutionContext context)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer ||
                projectilePrefab == null || context.Direction.sqrMagnitude <= 0.0001f)
                return false;

            if (!projectilePrefab.TryGetComponent<ModularProjectile>(out _) ||
                !HasPayloadReceiver(projectilePrefab.gameObject))
            {
                Debug.LogError(
                    "[EnemyProjectileAttackExecutor] Projectile prefab must contain ModularProjectile and a server combat payload receiver.",
                    this);
                return false;
            }

            ObjectPoolSystem pool = useShooterPool
                ? ObjectPoolSystem.GetPoolSystem(projectilePrefab.gameObject)
                : null;
            NetworkObject instance = pool != null
                ? pool.GetInstance(projectilePrefab.gameObject, true)
                : Instantiate(projectilePrefab);
            if (instance == null) return false;

            Vector3 origin = muzzle != null ? muzzle.position : context.Origin;
            Vector3 direction = context.Direction.normalized;
            instance.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));

            var payload = new ServerCombatProjectilePayload(
                context.Source,
                context.AttackId,
                context.AttackSequence,
                context.Damage,
                context.Knockback,
                DamageTags.Projectile);
            MonoBehaviour[] behaviours = instance.GetComponents<MonoBehaviour>();
            bool configured = false;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IServerCombatProjectilePayloadReceiver receiver)) continue;
                receiver.ConfigureServer(payload);
                configured = true;
            }

            if (!configured || !instance.TryGetComponent<ModularProjectile>(out ModularProjectile projectile))
            {
                RecycleInvalidInstance(instance, pool);
                return false;
            }

            if (instance.TryGetComponent<StraightLineMovement>(out var movement))
                movement.SetVelocityRate(projectileSpeed);
            var shootingContext = new ShootingContext
            {
                owner = gameObject,
                origin = origin,
                direction = direction,
                damage = context.Damage,
                ownerClientId = NetworkManager.ServerClientId,
                hitMask = impactMask
            };
            projectile.LaunchWithContext(origin, direction, Vector3.zero, null, shootingContext, gameObject);
            instance.Spawn();
            return true;
        }

        private static bool HasPayloadReceiver(GameObject prefab)
        {
            MonoBehaviour[] behaviours = prefab.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IServerCombatProjectilePayloadReceiver) return true;
            return false;
        }

        private static void RecycleInvalidInstance(NetworkObject instance, ObjectPoolSystem pool)
        {
            if (instance == null) return;
            if (pool != null) pool.ReturnToPool(instance);
            else Destroy(instance.gameObject);
        }

        private void OnValidate()
        {
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
        }
    }
}
