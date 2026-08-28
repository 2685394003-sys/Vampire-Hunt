using UnityEngine;
using UnityEngine.AI;
using Blocks.Gameplay.Core;

public class DashAbility : MonoBehaviour, IMovementAbility
{
    [Header("Dash Settings")]
    [Tooltip("一次冲刺移动的总距离（米）。实际冲刺速度 = 距离 ÷ 时长，自动推导。")]
    [SerializeField] private float dashDistance = 10f;
    [Tooltip("冲刺持续时长（秒）。")]
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 1.0f;
    [SerializeField] private float staminaCost = 15f;
    [SerializeField] private bool requireGrounded = false;
    [SerializeField] private bool allowAirDash = true;
    [SerializeField] private int maxAirDashes = 1;

    [Header("No-Clip Dash")]
    [Tooltip("冲刺期间无实体（禁用碰撞），可穿过障碍与敌人。")]
    [SerializeField] private bool noClipDash = true;
    [Tooltip("冲刺结束后校正落点，避免卡进建筑/墙体。")]
    [SerializeField] private bool resolveLandingPoint = true;
    [Tooltip("落点校正时的 NavMesh 采样距离（米）。")]
    [SerializeField] private float landingSampleDistance = 2f;

    [Header("Effects")]
    [SerializeField] private GameObject dashStartEffect;
    [SerializeField] private SoundDef dashStartSound;

    // Higher priority overrides standard movement
    public int Priority => 20;
    public float StaminaCost => staminaCost;

    /// <summary>冲刺速度（米/秒），由距离 ÷ 时长自动推导。</summary>
    private float DashSpeed => dashDuration > 0.0001f ? dashDistance / dashDuration : 0f;

    // Internal State
    private bool m_IsDashing;
    private float m_DashTimer;
    private CoreMovement m_Motor;
    private float m_CooldownTimer;
    private Vector3 m_DashDirection;
    private int m_RemainingAirDashes;
    private CharacterController m_CharacterController;
    private bool m_NoClipActive;

    public void Initialize(CoreMovement motor)
    {
        m_Motor = motor;
        m_Motor.OnGroundedStateChanged += OnGroundedStateChanged;
        m_RemainingAirDashes = maxAirDashes;
        m_CharacterController = m_Motor != null ? m_Motor.GetComponent<CharacterController>() : null;
    }

    // The physics logic applied every frame
    public MovementModifier Process()
    {
        var modifier = new MovementModifier();

        // Handle Cooldown
        if (m_CooldownTimer > 0)
        {
            m_CooldownTimer -= Time.deltaTime;
        }

        // Handle Active Dash
        if (m_IsDashing)
        {
            m_DashTimer -= Time.deltaTime;

            if (m_DashTimer <= 0)
            {
                EndDash();
            }
            else
            {
                // 无实体冲刺：直接改位置绕过 CharacterController 碰撞；仍返回速度供朝向旋转（CC 已禁用，不会重复 Move）
                if (noClipDash)
                {
                    transform.position += m_DashDirection * DashSpeed * Time.deltaTime;
                }
                // Apply Dash Velocity
                modifier.ArealVelocity = m_DashDirection * DashSpeed;
                // Disable gravity during dash
                modifier.OverrideGravity = true;
            }
        }

        return modifier;
    }

    public bool TryActivate()
    {
        // Validation Checks
        if (m_CooldownTimer > 0 || m_IsDashing) return false;
        if (requireGrounded && !m_Motor.IsGrounded) return false;
        if (!m_Motor.IsGrounded && !allowAirDash) return false;
        if (!m_Motor.IsGrounded && m_RemainingAirDashes <= 0) return false;

        // Calculate Direction
        Vector3 dashDir = CalculateDashDirection();

        // Fallback if no input: dash forward
        if (dashDir.magnitude < 0.1f)
        {
            dashDir = m_Motor.RotationTransform != null
                ? m_Motor.RotationTransform.forward
                : m_Motor.transform.forward;
        }

        // Start Dash
        StartDash(dashDir.normalized);
        return true;
    }

    private Vector3 CalculateDashDirection()
    {
        // If there is movement input, dash in that direction relative to camera/character
        if (m_Motor.MoveInput.magnitude > 0.1f)
        {
            Vector3 inputDirection = new Vector3(m_Motor.MoveInput.x, 0.0f, m_Motor.MoveInput.y);
            switch (m_Motor.directionMode)
            {
                case CoreMovement.MovementDirectionMode.CharacterRelative:
                    return m_Motor.transform.rotation * inputDirection;
                case CoreMovement.MovementDirectionMode.CameraRelative:
                    return Quaternion.Euler(0.0f, m_Motor.TargetRotationY, 0.0f) * inputDirection;
                default:
                    return inputDirection;
            }
        }

        // Otherwise default to forward
        Transform rotationTransform = m_Motor.RotationTransform != null
            ? m_Motor.RotationTransform
            : m_Motor.transform;

        return rotationTransform.forward;
    }

    private void StartDash(Vector3 direction)
    {
        // 兜底：若上次冲刺异常中断残留了无实体状态，先恢复碰撞
        if (m_NoClipActive && m_CharacterController != null)
        {
            m_CharacterController.enabled = true;
            m_NoClipActive = false;
        }

        m_IsDashing = true;
        m_DashTimer = dashDuration;
        m_CooldownTimer = dashCooldown;
        m_DashDirection = new Vector3(direction.x, 0f, direction.z).normalized;

        // 无实体冲刺：禁用 CharacterController 碰撞
        if (noClipDash && m_CharacterController != null)
        {
            m_CharacterController.enabled = false;
            m_NoClipActive = true;
        }

        if (!m_Motor.IsGrounded)
        {
            m_RemainingAirDashes--;
        }

        // Reset vertical velocity for a snappy dash feel
        m_Motor.SetVerticalVelocity(0f);

        // Play Effects
        if (dashStartEffect != null)
        {
            CoreDirector.CreatePrefabEffect(dashStartEffect)
                .WithPosition(m_Motor.transform.position)
                .WithRotation(Quaternion.LookRotation(m_DashDirection))
                .WithName("DashStart")
                .WithDuration(dashDuration + 0.5f)
                .Create();
        }

        if (dashStartSound != null)
        {
            CoreDirector.RequestAudio(dashStartSound)
                .AttachedTo(m_Motor.transform)
                .Play();
        }
    }

    private void EndDash()
    {
        m_IsDashing = false;
        m_DashTimer = 0f;

        if (m_NoClipActive)
        {
            // 先校正落点（碰撞仍禁用，可直接改位置），再恢复碰撞
            if (resolveLandingPoint)
            {
                ResolveLandingPoint();
            }

            m_NoClipActive = false;
            if (m_CharacterController != null)
            {
                m_CharacterController.enabled = true;
            }
        }
    }

    /// <summary>
    /// 冲刺结束后校正落点：若玩家停在建筑/墙体内（不可行走面），拉到最近的可行走点。
    /// </summary>
    private void ResolveLandingPoint()
    {
        Vector3 pos = transform.position;

        // 1) 附近采样：当前位置是否已在可行走面上
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, landingSampleDistance, NavMesh.AllAreas))
        {
            // 采样点距离当前位置很近（就在可行走面）则不校正，避免每次冲刺结束都做位置微调/teleport
            if (Vector3.Distance(pos, hit.position) > 0.1f)
            {
                m_Motor.SetPosition(hit.position);
            }
            return;
        }

        // 2) 沿冲刺反方向逐步回退，找最近可行走点（最多退 dashDistance）
        Vector3 back = -m_DashDirection;
        for (float d = 1f; d <= dashDistance + 1f; d += 1f)
        {
            Vector3 candidate = pos + back * d;
            if (NavMesh.SamplePosition(candidate, out hit, 1f, NavMesh.AllAreas))
            {
                m_Motor.SetPosition(hit.position);
                return;
            }
        }

        // 3) 兜底：大范围采样（极端情况，如冲进超大建筑深处）
        if (NavMesh.SamplePosition(pos, out hit, 50f, NavMesh.AllAreas))
        {
            m_Motor.SetPosition(hit.position);
        }
    }

    private void OnGroundedStateChanged(bool isGrounded)
    {
        if (isGrounded)
        {
            // Reset air dashes
            m_RemainingAirDashes = maxAirDashes;
        }
    }

    private void OnDestroy()
    {
        if (m_Motor != null)
        {
            m_Motor.OnGroundedStateChanged -= OnGroundedStateChanged;
        }

        // 兜底：销毁时若仍处于无实体状态，恢复碰撞避免残留
        if (m_NoClipActive && m_CharacterController != null)
        {
            m_CharacterController.enabled = true;
            m_NoClipActive = false;
        }
    }
}

