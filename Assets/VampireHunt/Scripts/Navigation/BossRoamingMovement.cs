using System;
using UnityEngine;
using UnityEngine.AI;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Navigation
{
    /// <summary>Server-only roaming movement. It never decides encounter state or damage.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class BossRoamingMovement : MonoBehaviour
    {
        [SerializeField] private BossEncounterConfigAsset config;
        [SerializeField] private CharacterController characterController;

        private IPlayerTargetQuery m_Targets;
        private Float3 m_LastTargetPosition;
        private float m_LastSampleTime;
        private float m_VerticalVelocity;
        private bool m_HasTargetSample;

        private void Awake()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            ResolveTargetPort();
        }

        public void SetConfig(BossEncounterConfigAsset value) => config = value;

        public bool TrySpawnInPlayerAnnulusServer(uint seed)
        {
            if (config == null) return false;
            ResolveTargetPort();
            if (m_Targets == null || !m_Targets.TryGetNearest(ToFloat3(transform.position), float.MaxValue,
                    out BossPlayerTarget target)) return false;

            var random = new System.Random(unchecked((int)seed));
            for (int attempt = 0; attempt < 24; attempt++)
            {
                double angle = random.NextDouble() * Math.PI * 2d;
                float distance = Mathf.Lerp(config.SpawnMinDistance, config.SpawnMaxDistance,
                    (float)random.NextDouble());
                Vector3 candidate = ToVector3(target.Position) +
                                    new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)) * distance;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    transform.position = hit.position;
                    ResetSamples();
                    return true;
                }
            }

            return false;
        }

        public void TickServer(bool shouldEvade)
        {
            if (!shouldEvade || config == null || characterController == null || !characterController.enabled)
            {
                ApplyGravityOnly();
                return;
            }

            ResolveTargetPort();
            if (m_Targets == null || !m_Targets.TryGetNearest(ToFloat3(transform.position), config.DetectionRange,
                    out BossPlayerTarget target))
            {
                ApplyGravityOnly();
                ResetSamples();
                return;
            }

            Vector3 targetPosition = ToVector3(target.Position);
            Vector3 away = transform.position - targetPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            away.Normalize();

            float playerSpeed = EstimatePlayerSpeed(target.Position);
            float speed = Mathf.Clamp(Mathf.Max(config.NormalMoveSpeed, playerSpeed),
                config.NormalMoveSpeed, config.MaxMirrorSpeed);
            Vector3 movement = away * speed;
            if (characterController.isGrounded && m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
            else m_VerticalVelocity += Physics.gravity.y * Time.deltaTime;
            movement.y = m_VerticalVelocity;
            characterController.Move(movement * Time.deltaTime);

            Quaternion facing = Quaternion.LookRotation(-away, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, 540f * Time.deltaTime);
        }

        public bool TryTeleportAwayServer(uint seed) => TrySpawnInPlayerAnnulusServer(seed);

        private float EstimatePlayerSpeed(in Float3 position)
        {
            float now = Time.unscaledTime;
            float speed = config.NormalMoveSpeed;
            if (m_HasTargetSample)
            {
                float delta = Mathf.Max(0.001f, now - m_LastSampleTime);
                float x = position.X - m_LastTargetPosition.X;
                float z = position.Z - m_LastTargetPosition.Z;
                speed = Mathf.Sqrt(x * x + z * z) / delta;
            }
            m_LastTargetPosition = position;
            m_LastSampleTime = now;
            m_HasTargetSample = true;
            return speed;
        }

        private void ApplyGravityOnly()
        {
            if (characterController == null || !characterController.enabled) return;
            if (characterController.isGrounded && m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
            else m_VerticalVelocity += Physics.gravity.y * Time.deltaTime;
            characterController.Move(Vector3.up * (m_VerticalVelocity * Time.deltaTime));
        }

        private void ResetSamples()
        {
            m_HasTargetSample = false;
            m_LastSampleTime = 0f;
            m_VerticalVelocity = 0f;
        }

        private void ResolveTargetPort()
        {
            if (m_Targets != null) return;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IPlayerTargetQuery query) { m_Targets = query; break; }
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
