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
        TryAttack();
    }

    /// <summary>Returns whether an authoritative attack intent was forwarded.</summary>
    public bool TryAttack()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return false;

        EnemyHealth health = GetComponentInParent<EnemyHealth>();
        if (health == null || health.Runtime == null || health.IsDead || health.IsFrozen)
            return false;

        EnemySnapshot snapshot = health.Runtime.Snapshot;
        if (!snapshot.TargetId.IsValid) return false;

        Vector3 origin = EnemyAttackPoint != null ? EnemyAttackPoint.position : transform.position;
        Vector3 targetPosition = new(
            snapshot.TargetPosition.X,
            snapshot.TargetPosition.Y,
            snapshot.TargetPosition.Z);
        float distance = Vector3.Distance(origin, targetPosition);

        EnemyCombatContextData context = new EnemyCombatContextData(
            health.Runtime.Id,
            snapshot.TargetId,
            ToWorldPosition(origin),
            distance,
            true,
            Time.time,
            health.Runtime.Snapshot.LastAttackAt);
        AttackIntent intent = health.Runtime.CreateAttackIntent(in context);
        if (!intent.SourceId.IsValid) return false;

        foreach (MonoBehaviour component in GetComponentsInParent<MonoBehaviour>(true))
        {
            if (!(component is IEnemyAttackPort port)) continue;
            port.Execute(in intent);
            return true;
        }

        return false;
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
