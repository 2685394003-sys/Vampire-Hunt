using UnityEngine;

public sealed class ChasingEnemy : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2.2f;
    [SerializeField] private float contactRange = 1.15f;
    [SerializeField] private float contactCooldown = 0.7f;

    private Transform target;
    private float nextContactTime;

    private void Awake()
    {
        ResolveTarget();
    }

    private void Update()
    {
        if (!ResolveTarget())
        {
            return;
        }

        Vector3 toTarget = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        float distance = toTarget.magnitude;
        if (distance > contactRange)
        {
            Vector3 movement = toTarget.normalized * (moveSpeed * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, target.position, movement.magnitude);
            return;
        }

        if (Time.time >= nextContactTime)
        {
            nextContactTime = Time.time + contactCooldown;
            // Contact is detected here; health is deliberately not implemented yet.
        }
    }

    private bool ResolveTarget()
    {
        if (target != null)
        {
            return true;
        }

        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player == null)
        {
            return false;
        }

        target = player.transform;
        return true;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void ReceiveHit()
    {
        // Hit has been detected; health and reactions are deliberately deferred.
    }
}
