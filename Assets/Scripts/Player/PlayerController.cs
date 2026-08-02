using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public sealed class PlayerController : MonoBehaviour
{
    [Header("输入绑定 / Input Bindings")]
    public InputAction moveAction;
    public InputAction attackAction;

    [Header("组件引用 / Component References")]
    public Animator anim;
    public PlayerAttact playerAttack;

    [Header("状态 / State")]
    public bool isKnockedBack;
    public Vector3 knockbackVelocity;

    [Header("攻击配置 / Attack Config")]
    [SerializeField] private float attackRange = 2.1f;
    [SerializeField] private float attackRadius = 1.15f;
    [SerializeField] private float attackCooldown = 0.35f;
    private Transform attackMarker;
    public Vector3 facingDirection = Vector3.forward;
    private float nextAttackTime;

    private Camera viewCamera;
    private Vector2 moveInput;

    private void Awake()
    {
        viewCamera = Camera.main;
    }

    private void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        if (isKnockedBack)
        {
            transform.position += knockbackVelocity * Time.fixedDeltaTime;
            return;
        }

        float horizontal = moveInput.x;
        float vertical = moveInput.y;

        // 俯视相机3D方向转换
        Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        Vector3 moveDir = cameraForward * vertical + cameraRight * horizontal;
        if (moveDir.sqrMagnitude > 0.001f)
            moveDir.Normalize();

        // 角色翻转
        if ((horizontal > 0 && transform.localScale.x < 0) || (horizontal < 0 && transform.localScale.x > 0))
        {
            Flip();
        }

        // 同步动画移动参数
        anim.SetFloat("horizontal", horizontal);
        anim.SetFloat("vertical", vertical);

        if (StatsManager.Instance != null)
        {
            float speed = StatsManager.Instance.speed;
            transform.position += moveDir * speed * Time.fixedDeltaTime;
        }
    }

    void Flip()
    {
        facingDirection *= -1;
        transform.localScale = new Vector3(transform.localScale.x * -1, transform.localScale.y, transform.localScale.z);
    }

    public void Knockback(Transform enemy, float force, float stunTime)
    {
        if (!gameObject.activeSelf) return;
        isKnockedBack = true;
        Vector3 dir = (transform.position - enemy.position).normalized;
        knockbackVelocity = dir * force;
        StartCoroutine(KnockbackCounter(stunTime));
    }

    IEnumerator KnockbackCounter(float stunTime)
    {
        yield return new WaitForSeconds(stunTime);
        knockbackVelocity = Vector3.zero;
        isKnockedBack = false;
    }

    /// <summary>
    /// 鼠标左键按下：仅触发攻击动画，冷却锁在这里
    /// </summary>
    public void AttackTrigger(InputAction.CallbackContext ctx)
    {
        if (Time.time < nextAttackTime)
            return;
        if (isKnockedBack)
            return;

        nextAttackTime = Time.time + attackCooldown;
        playerAttack.Attack(); // 调用另一个脚本攻击
    }

    private void OnEnable()
    {
        moveAction.Enable();
        attackAction.Enable();
        attackAction.performed += AttackTrigger;
    }

    private void OnDisable()
    {
        moveAction.Disable();
        attackAction.Disable();
        attackAction.performed -= AttackTrigger;
    }
}
