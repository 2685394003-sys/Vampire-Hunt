using System;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity.Combat;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Owner-side trusted hit detector for a networked sword wave.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModularProjectile))]
    public sealed class SwordWaveEffect : NetworkBehaviour, IProjectileEffect
    {
        [Header("Hit Detection")]
        [Min(0.05f)] [SerializeField] private float hitRadius = 0.65f;
        [Tooltip("勾选后：运行时把 Visual 子物体 Scale 设为判定半径（Hit Radius × Projectile Size），实现\"子弹大小 = 伤害判定大小\"。扇形剑气不勾（视觉由扇形网格生成）。")]
        [SerializeField] private bool scaleVisualToHitRadius;
        [Tooltip("勾选后：运行时按 Fan Angle 生成扇形网格替换 Visual 的静态 mesh（用于剑气扇形）。弹丸类武器（狙击/自动步枪）不要勾，否则会被替换成扇形。")]
        [SerializeField] private bool buildFanVisual;
        [SerializeField] private LayerMask targetMask = 1 << 8;
        [Tooltip("Wwise 事件名（命中音），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string hitEventName = "";
        [Header("穿透消耗")]
        [Tooltip("全局共享配置，与激光（LaserBeam）共用同一份，改一处即可同时影响五把武器。留空则回退到内置默认值 1 / 30 / 30。")]
        [SerializeField] private PierceCostConfig pierceCost;
        [Header("Fallback Lifetime")]
        [Min(0.1f)] [SerializeField] private float defaultMaximumDistance = 12f;
        [Min(0.1f)] [SerializeField] private float maximumLifetime = 2f;

        private readonly NetworkVariable<ulong> m_AttackerClientId = new NetworkVariable<ulong>(
            0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<AbilityCastNetworkMessage> m_Cast =
            new NetworkVariable<AbilityCastNetworkMessage>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Collider[] m_HitBuffer = new Collider[32];
        private readonly HashSet<MonoBehaviour> m_HitTargets = new HashSet<MonoBehaviour>();
        private ModularProjectile m_Projectile;
        private Rigidbody m_Rigidbody;
        private CombatModifierHost m_OwnerModifierHost;
        private ulong m_PendingAttackerClientId;
        private AbilityCastNetworkMessage m_PendingCast;
        private bool m_HasPendingConfiguration;
        private Vector3 m_StartPosition;
        private float m_ExpireTime;
        private int m_HitCount;
        private bool m_CompletionRequested;

        // 扇形视觉：仅当 buildFanVisual 勾选时，运行时按 Fan Angle 生成扇形网格（替换预制体的静态 mesh）
        private Mesh m_FanMesh;
        private bool m_FanBuilt;

        public bool IsDeferredDespawnEnabled => false;
        public int DeferredDespawnTicks => 0;
        public event Action<ModularProjectile> OnEffectComplete;

        public void ConfigureServer(ulong attackerClientId, in AbilityCastNetworkMessage message)
        {
            m_PendingAttackerClientId = attackerClientId;
            m_PendingCast = message;
            m_HasPendingConfiguration = true;
        }

        public void Initialize(ModularProjectile projectile)
        {
            m_Projectile = projectile;
            m_Rigidbody = GetComponent<Rigidbody>();
        }

        public void Setup(GameObject owner, IWeapon sourceWeapon, ShootingContext context) { }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && m_HasPendingConfiguration)
            {
                m_AttackerClientId.Value = m_PendingAttackerClientId;
                m_Cast.Value = m_PendingCast;
                m_HasPendingConfiguration = false;
            }

            ApplyRuntimeProjectileValues();
            if (IsOwner && !IsServer) OnLaunch();

            // 视觉扇形 mesh：值已就绪立即构建（服务器端）；客户端等 NetworkVariable 同步后构建
            m_Cast.OnValueChanged += OnCastChanged;
            if (m_Cast.Value.AbilityId != 0) BuildFanVisual();
        }

        public override void OnNetworkDespawn()
        {
            m_Cast.OnValueChanged -= OnCastChanged;
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            if (m_FanMesh != null)
            {
                Destroy(m_FanMesh);
                m_FanMesh = null;
            }
        }

        private void OnCastChanged(AbilityCastNetworkMessage previous, AbilityCastNetworkMessage current)
        {
            // 客户端收到 cast 同步后：重设弹速/视觉大小（与服务器端一致），并构建扇形视觉（剑气）
            ApplyRuntimeProjectileValues();
            BuildFanVisual();
        }

        /// <summary>把 Visual 子物体的静态 mesh 替换为按 Fan Angle 生成的扇形网格（半径 = hitRadius）。仅 buildFanVisual 勾选时生效。</summary>
        private void BuildFanVisual()
        {
            if (m_FanBuilt || !buildFanVisual || m_Cast.Value.AbilityId == 0 || m_Cast.Value.FanAngle <= 0f) return;

            Transform visual = transform.Find("Visual");
            if (visual == null) return;
            var filter = visual.GetComponent<MeshFilter>();
            if (filter == null) return;

            m_FanMesh = BuildFanMesh(hitRadius, m_Cast.Value.FanAngle * 0.5f);
            filter.sharedMesh = m_FanMesh;
            m_FanBuilt = true;
        }

        /// <summary>生成水平展开的扇形网格（含上下两面，厚度由 Visual.localScale.y 控制）。半角 halfAngleDeg，半径 radius。</summary>
        private static Mesh BuildFanMesh(float radius, float halfAngleDeg)
        {
            const int segments = 24;
            var verts = new List<Vector3>(segments * 2 + 2);
            var tris = new List<int>(segments * 6);

            verts.Add(Vector3.zero);                       // 0: 圆心（上面）
            verts.Add(Vector3.zero);                       // 1: 圆心（下面）
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;             // 0..1
                float angle = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, t) * Mathf.Deg2Rad;
                float x = Mathf.Sin(angle) * radius;
                float z = Mathf.Cos(angle) * radius;
                verts.Add(new Vector3(x, 0.5f, z));        // 上面弧点（Y=+0.5，厚度由 localScale.y 压扁）
                verts.Add(new Vector3(x, -0.5f, z));       // 下面弧点
            }
            for (int i = 0; i < segments; i++)
            {
                int arc0 = 2 + i * 2;
                int arc1 = arc0 + 2;
                // 上面（逆时针，法线朝上）
                tris.Add(0); tris.Add(arc0); tris.Add(arc1);
                // 下面（顺时针，法线朝下）
                tris.Add(1); tris.Add(arc1 + 1); tris.Add(arc0 + 1);
            }

            var mesh = new Mesh { name = "SwordWaveFan" };
            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ApplyRuntimeProjectileValues()
        {
            AbilityCastNetworkMessage cast = m_Cast.Value;
            if (TryGetComponent<StraightLineMovement>(out var movement) && cast.ProjectileSpeed > 0f)
                movement.SetVelocityRate(cast.ProjectileSpeed);

            // "子弹大小 = 伤害判定大小"：勾选 scaleVisualToHitRadius 的弹丸，Visual 子物体 Scale = 判定半径（Hit Radius × Projectile Size）。
            // 改 asset 的 Projectile Size 或 prefab 的 Hit Radius，判定与视觉同步变化且始终相等。
            if (scaleVisualToHitRadius && cast.ProjectileSize > 0f)
            {
                Transform visual = transform.Find("Visual");
                if (visual != null)
                {
                    float radius = hitRadius * Mathf.Max(0.1f, cast.ProjectileSize);
                    visual.localScale = Vector3.one * radius;
                }
            }
        }

        public void OnLaunch()
        {
            m_StartPosition = transform.position;
            m_ExpireTime = Time.unscaledTime + maximumLifetime;
            m_HitCount = 0;
            m_CompletionRequested = false;
            m_HitTargets.Clear();
            if (NetworkManager.LocalClient != null && NetworkManager.LocalClient.PlayerObject != null)
                m_OwnerModifierHost = NetworkManager.LocalClient.PlayerObject.GetComponent<CombatModifierHost>();
        }

        public void ProcessUpdate()
        {
            if (!IsSpawned || !IsOwner || m_CompletionRequested) return;
            DetectTrustedHits();
            float maximumDistance = m_Cast.Value.TravelDistance > 0f
                ? m_Cast.Value.TravelDistance
                : defaultMaximumDistance;
            if ((transform.position - m_StartPosition).sqrMagnitude >= maximumDistance * maximumDistance ||
                Time.unscaledTime >= m_ExpireTime) RequestCompletion();
        }

        public void Cleanup()
        {
            m_HitTargets.Clear();
            m_CompletionRequested = true;
        }

        public ContactEventHandlerInfo GetContactEventHandlerInfo() => new ContactEventHandlerInfo
        {
            ProvideNonRigidBodyContactEvents = false,
            HasContactEventPriority = IsOwner
        };

        public Rigidbody GetRigidbody() => m_Rigidbody;
        public void ContactEvent(ulong eventId, Vector3 averageNormal, Rigidbody collidingBody,
            Vector3 contactPoint, bool hasCollisionStay = false,
            Vector3 averagedCollisionStayNormal = default) { }

        private void DetectTrustedHits()
        {
            AbilityCastNetworkMessage cast = m_Cast.Value;
            float radius = hitRadius * Mathf.Max(0.1f, cast.ProjectileSize);
            int overlapCount = Physics.OverlapSphereNonAlloc(transform.position, radius, m_HitBuffer,
                targetMask, QueryTriggerInteraction.Collide);

            // 扇形判定：仅 buildFanVisual（剑气）勾选时启用，全角 = cast.FanAngle。
            // 弹丸武器（狙击/步枪）的 SpreadAngle 是精度散布，不是扇形角度，必须保持全向球判定。
            Vector3 forward = transform.forward;
            forward.y = 0f;
            bool fanMode = buildFanVisual && cast.FanAngle > 0.001f && forward.sqrMagnitude > 0.0001f;
            forward = forward.normalized;
            float halfAngle = cast.FanAngle * 0.5f;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider hitCollider = m_HitBuffer[i];
                if (hitCollider == null || !TryFindTarget(hitCollider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                if (!m_HitTargets.Add(target)) continue;

                // 角度过滤：目标必须在剑气朝向两侧 ±半角 的扇形内（水平面）
                if (fanMode)
                {
                    Vector3 toTarget = hitCollider.transform.position - transform.position;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude > 0.0001f && Vector3.Angle(forward, toTarget) > halfAngle)
                        continue;
                }

                Vector3 direction = transform.forward.sqrMagnitude > 0.0001f
                    ? transform.forward.normalized
                    : Vector3.forward;
                if (trustedTarget != null)
                {
                    var request = new DamageRequest(
                        new GameplayEntityId(m_AttackerClientId.Value + 1UL), GameplayEntityId.None,
                        cast.AbilityId, cast.Sequence, cast.Damage, (DamageTags)cast.Tags);
                    int statusCount = Mathf.Min(4, cast.OnHitStatuses.Count);
                    var statuses = new StatusEffectSpec[statusCount];
                    for (int statusIndex = 0; statusIndex < statusCount; statusIndex++)
                        statuses[statusIndex] = cast.OnHitStatuses.Get(statusIndex).ToDomain();
                    var force = direction * cast.Knockback;
                    trustedTarget.SubmitTrustedHit(new TrustedCombatHit(request, (ElementId)cast.Element,
                        statuses, new Float3(force.x, force.y, force.z)));
                }
                else
                {
                    fallback.OnHit(new HitInfo
                    {
                        amount = cast.Damage,
                        hitPoint = hitCollider.ClosestPoint(transform.position),
                        hitNormal = -direction,
                        attackerId = m_AttackerClientId.Value,
                        impactForce = direction * cast.Knockback
                    });
                }

                NotifyTrustedOutcome(cast);
                if (!string.IsNullOrEmpty(hitEventName)) WwiseAudioBridge.PostEvent(hitEventName, gameObject);
                // 按目标类型扣穿透额度：Boss 本体/手消耗远高于小怪，实现「Boss 挡住穿透」。
                m_HitCount += GetPierceCost(target);
                if (m_HitCount >= Mathf.Max(1, cast.PierceCount))
                {
                    RequestCompletion();
                    return;
                }
            }
        }

        // 穿透消耗：优先读全局共享配置；漏挂资产时回退内置默认值，避免 Boss 阻挡静默失效。
        private int NormalCost => pierceCost != null ? pierceCost.NormalPierceCost : 1;
        private int BossCost => pierceCost != null ? pierceCost.BossPierceCost : 30;
        private int BossHandCost => pierceCost != null ? pierceCost.BossHandPierceCost : 30;

        /// <summary>
        /// 返回目标对穿透的消耗值：Boss 本体 → BossCost，Boss 手 → BossHandCost，其余（普通敌人）→ NormalCost。
        /// 三者统一取自全局共享配置 PierceCostConfig，与激光（LaserBeam）保持同一口径。
        /// </summary>
        private int GetPierceCost(MonoBehaviour target)
        {
            if (target.GetComponentInParent<BossVitalsReceiver>() != null) return BossCost;
            if (target.GetComponentInParent<BossHandNetworkActor>() != null) return BossHandCost;
            return NormalCost;
        }

        private static bool TryFindTarget(Collider collider, out MonoBehaviour target,
            out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)
        {
            MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ITrustedCombatHitTarget trusted)
                {
                    target = behaviours[i];
                    trustedTarget = trusted;
                    fallback = null;
                    return true;
                }
            }
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IHittable hittable)
                {
                    target = behaviours[i];
                    trustedTarget = null;
                    fallback = hittable;
                    return true;
                }
            }
            target = null;
            trustedTarget = null;
            fallback = null;
            return false;
        }

        private void NotifyTrustedOutcome(in AbilityCastNetworkMessage cast)
        {
            if (m_OwnerModifierHost == null) return;
            var request = new DamageRequest(new GameplayEntityId(m_AttackerClientId.Value + 1UL),
                GameplayEntityId.None, cast.AbilityId, cast.Sequence, cast.Damage, (DamageTags)cast.Tags);
            m_OwnerModifierHost.NotifyOutcome(new ResolvedDamage(request, cast.Damage,
                (DamageTags)cast.Tags, false));
        }

        private void RequestCompletion()
        {
            if (m_CompletionRequested) return;
            m_CompletionRequested = true;
            if (IsServer) OnEffectComplete?.Invoke(m_Projectile);
            else RequestDespawnRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDespawnRpc()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.1f, 0.15f, 0.75f);
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
    }
}
