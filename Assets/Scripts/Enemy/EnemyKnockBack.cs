using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyKnockBack : MonoBehaviour
{
    private Rigidbody rb;
    private FlowFieldEnemy Enemymovement;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        Enemymovement = GetComponent<FlowFieldEnemy>();
    }

    public void EnemyKnockback(Transform playerTransform, float knockbackForce, float Stuntime, float knockbackTime)
    {
        if (Enemymovement != null)
            Enemymovement.ChangeState(EnemyState.Knockback);
        if (gameObject.activeSelf)
        {
            StartCoroutine(StunTime(Stuntime, knockbackTime));
        }
        if (rb != null)
        {
            Vector3 direction = (transform.position - playerTransform.position).normalized;
            rb.linearVelocity = direction * knockbackForce;
        }
    }

    IEnumerator StunTime(float Stuntime, float knockbackTime)
    {
        yield return new WaitForSeconds(knockbackTime);
        if (rb != null) rb.linearVelocity = Vector3.zero;
        yield return new WaitForSeconds(Stuntime);
        if (Enemymovement != null) Enemymovement.ChangeState(EnemyState.Idle);
    }
}
