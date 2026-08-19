using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Enemy prefab attack adapter.  It converts the serialized attack point and
/// current target view into an AttackIntent, then forwards that intent to the
/// composed Enemies/Combat application port.  It never changes player health.
/// </summary>
public sealed class EnemyCombat : MonoBehaviour
{
    public Transform EnemyAttackPoint;

    /// <summary>
    /// Compatibility entry point for old AnimationEvents.  The server-side
    /// application decides range/cooldown/damage; this component only forwards.
    /// </summary>
    public void Attack()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;

        EnemyHealth health = GetComponentInParent<EnemyHealth>();
        FlowFieldEnemy enemy = GetComponentInParent<FlowFieldEnemy>();
        if (health == null || health.Runtime == null || health.IsDead || health.IsFrozen)
            return;

        Transform target = enemy != null ? enemy.CurrentTarget : null;
        if (target == null) return;

        Vector3 origin = EnemyAttackPoint != null ? EnemyAttackPoint.position : transform.position;
        float distance = Vector3.Distance(origin, target.position);
        EntityId targetId = EnemyLegacyEntityIds.Resolve(target.gameObject);
        if (!targetId.IsValid) return;

        EnemyCombatContextData context = new EnemyCombatContextData(
            health.Runtime.Id,
            targetId,
            ToWorldPosition(origin),
            distance,
            true,
            Time.time,
            health.Runtime.Snapshot.LastAttackAt);
        AttackIntent intent = health.Runtime.CreateAttackIntent(in context);
        if (!intent.SourceId.IsValid) return;

        foreach (MonoBehaviour component in GetComponentsInParent<MonoBehaviour>(true))
        {
            if (!(component is IEnemyAttackPort port)) continue;
            port.Execute(in intent);
            break;
        }
    }

    private void OnDrawGizmosSelected()
    {
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        if (EnemyAttackPoint == null || stats == null) return;

        EnemyHealth health = GetComponentInParent<EnemyHealth>();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            EnemyAttackPoint.position,
            health != null
                ? health.GetStatValue(EnemyStatType.WeaponRange)
                : EnemyRunStats.GetValue(stats, EnemyStatType.WeaponRange));
    }

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);
}
