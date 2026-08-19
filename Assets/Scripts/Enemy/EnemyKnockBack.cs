using System.Collections;
using UnityEngine;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Motor adapter for the shared knockback capability.  Knockback strength and
/// immunity are resolved by Combat; this component only executes the impulse
/// on the pooled Unity body and restores the movement state afterwards.
/// </summary>
public class EnemyKnockBack : MonoBehaviour,
    VampireHunt.Combat.Contracts.IKnockbackReceiver, INetworkPoolLifecycle
{
    private Rigidbody body;
    private FlowFieldEnemy enemyMovement;
    private Coroutine restoreCoroutine;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        enemyMovement = GetComponent<FlowFieldEnemy>();
    }

    public void EnemyKnockback(
        Transform playerTransform,
        float knockbackForce,
        float stunTime,
        float knockbackTime)
    {
        if (!NetworkAuthority.IsServerOrOffline() || playerTransform == null) return;

        EntityId sourceId = EnemyLegacyEntityIds.Resolve(playerTransform.gameObject);
        EntityId targetId = EnemyLegacyEntityIds.Resolve(gameObject);

        Vector3 direction = transform.position - playerTransform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        direction.Normalize();

        KnockbackImpulse impulse = new KnockbackImpulse(
            sourceId,
            targetId,
            ToWorldPosition(direction),
            Mathf.Max(0f, knockbackForce),
            Mathf.Max(0f, knockbackTime),
            false);
        ApplyKnockback(in impulse);
        if (stunTime > 0f)
            StartRestore(stunTime, knockbackTime);
    }

    public void ApplyKnockback(in KnockbackImpulse impulse)
    {
        if (!NetworkAuthority.IsServerOrOffline() || impulse.IsImmune) return;

        enemyMovement?.EnterKnockbackState();
        if (body != null)
        {
            Vector3 direction = new Vector3(
                impulse.Direction.X,
                impulse.Direction.Y,
                impulse.Direction.Z);
            body.linearVelocity = direction * impulse.Force;
        }

        StartRestore(impulse.Duration, 0f);
    }

    public void OnTakenFromNetworkPool() => ResetPoolState();

    public void OnReturnedToNetworkPool() => ResetPoolState();

    private void ResetPoolState()
    {
        if (restoreCoroutine != null)
        {
            StopCoroutine(restoreCoroutine);
            restoreCoroutine = null;
        }

        if (body != null) body.linearVelocity = Vector3.zero;
        enemyMovement?.ChangeState(EnemyState.Idle);
    }

    private void StartRestore(float stunTime, float knockbackTime)
    {
        if (!isActiveAndEnabled) return;
        if (restoreCoroutine != null) StopCoroutine(restoreCoroutine);
        restoreCoroutine = StartCoroutine(RestoreAfter(stunTime, knockbackTime));
    }

    private IEnumerator RestoreAfter(float stunTime, float knockbackTime)
    {
        if (knockbackTime > 0f) yield return new WaitForSeconds(knockbackTime);
        if (body != null) body.linearVelocity = Vector3.zero;
        if (stunTime > 0f) yield return new WaitForSeconds(stunTime);
        enemyMovement?.ChangeState(EnemyState.Idle);
        restoreCoroutine = null;
    }

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);
}
