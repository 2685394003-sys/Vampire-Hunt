using System;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Aiming
{
    /// <summary>
    /// Resolves the owner's pointer onto the world and owns mouse-facing rotation.
    /// Consumers read the cached snapshot through ICombatAimSource.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CoreMovement))]
    public sealed class TopDownAimAddon : NetworkBehaviour, IPlayerAddon, ICombatAimSource
    {
        [Header("World Projection")]
        // Ground(9) = 地形专用层。用 1<<0(Default) 会把地图道具（桶/车/箱）也算进来，
        // 瞄准点会落在道具表面而不是地面。
        [SerializeField] private LayerMask groundLayers = 1 << 9;
        [SerializeField, Min(1f)] private float maximumRayDistance = 1000f;
        [SerializeField] private bool usePlayerPlaneFallback = true;

        [Header("Rotation")]
        [SerializeField, Min(0f)] private float rotationDegreesPerSecond = 1080f;
        [SerializeField, Min(0f)] private float minimumAimDistance = 0.05f;

        private CorePlayerManager m_PlayerManager;
        private CoreMovement m_Movement;
        private Camera m_OwnerCamera;
        private Vector3 m_WorldPoint;
        private Vector3 m_Direction;
        private bool m_HasAim;
        private bool m_RotationInstalled;
        private Func<Quaternion> m_RotationResolver;

        public void Initialize(CorePlayerManager playerManager)
        {
            m_PlayerManager = playerManager;
            m_Movement = playerManager != null ? playerManager.CoreMovement : GetComponent<CoreMovement>();
        }

        public void OnPlayerSpawn()
        {
            if (m_PlayerManager == null || !m_PlayerManager.IsOwner || m_Movement == null) return;

            m_RotationResolver ??= ResolveRotation;
            m_Movement.RotationOverride = m_RotationResolver;
            m_RotationInstalled = true;
        }

        public void OnPlayerDespawn()
        {
            RemoveRotationOverride();
            m_OwnerCamera = null;
            m_HasAim = false;
        }

        public void OnLifeStateChanged(PlayerLifeState previousState, PlayerLifeState newState)
        {
            if (newState == PlayerLifeState.Eliminated)
            {
                m_HasAim = false;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || Mouse.current == null) return;

            if (m_OwnerCamera == null || !m_OwnerCamera.isActiveAndEnabled)
            {
                m_OwnerCamera = Camera.main;
            }

            if (m_OwnerCamera == null) return;

            Ray pointerRay = m_OwnerCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!TryResolveWorldPoint(pointerRay, out Vector3 worldPoint))
            {
                m_HasAim = false;
                return;
            }

            Vector3 direction = worldPoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < minimumAimDistance * minimumAimDistance)
            {
                m_HasAim = false;
                return;
            }

            m_WorldPoint = worldPoint;
            m_Direction = direction.normalized;
            m_HasAim = true;
        }

        public bool TryGetAim(out AimSnapshot snapshot)
        {
            if (!m_HasAim)
            {
                snapshot = default;
                return false;
            }

            snapshot = new AimSnapshot(ToFloat3(m_WorldPoint), ToFloat3(m_Direction));
            return true;
        }

        private bool TryResolveWorldPoint(Ray pointerRay, out Vector3 worldPoint)
        {
            if (Physics.Raycast(
                    pointerRay,
                    out RaycastHit hit,
                    maximumRayDistance,
                    groundLayers,
                    QueryTriggerInteraction.Ignore))
            {
                worldPoint = hit.point;
                return true;
            }

            if (usePlayerPlaneFallback)
            {
                var playerPlane = new Plane(Vector3.up, transform.position);
                if (playerPlane.Raycast(pointerRay, out float enter) && enter <= maximumRayDistance)
                {
                    worldPoint = pointerRay.GetPoint(enter);
                    return true;
                }
            }

            worldPoint = default;
            return false;
        }

        private Quaternion ResolveRotation()
        {
            Transform rotationTarget = m_Movement != null && m_Movement.RotationTransform != null
                ? m_Movement.RotationTransform
                : transform;
            Quaternion current = rotationTarget.rotation;
            if (!m_HasAim) return current;

            Quaternion target = Quaternion.LookRotation(m_Direction, Vector3.up);
            if (rotationDegreesPerSecond <= 0f) return target;

            return Quaternion.RotateTowards(
                current,
                target,
                rotationDegreesPerSecond * Time.deltaTime);
        }

        private void RemoveRotationOverride()
        {
            if (!m_RotationInstalled || m_Movement == null) return;

            if (m_Movement.RotationOverride == m_RotationResolver)
            {
                m_Movement.RotationOverride = null;
            }
            m_RotationInstalled = false;
        }

        private void OnDestroy()
        {
            RemoveRotationOverride();
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
    }
}
