using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Encounter;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Network boundary for player-to-Boss hits. Clients request; server validates and mutates.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(BossEncounterDirector))]
    public sealed class BossVitalsReceiver : NetworkBehaviour, IDamageReceiver,
        ITrustedCombatHitTarget, ICombatEntityIdentity
    {
        [SerializeField] private BossEncounterDirector director;
        [SerializeField] private BossBodyStateHost bodyState;
        [SerializeField] private BossAbilityServerDriver abilityDriver;
        [Tooltip("Boss 本体的状态宿主（CombatStatusHost）；命中时把武器元素状态挂上去，并受其 elementResist 减免。")]
        [SerializeField] private CombatStatusHost statusHost;

        private readonly Dictionary<ulong, ulong> m_LastSequenceByClientAttack =
            new Dictionary<ulong, ulong>();

        public GameplayEntityId CombatEntityId => bodyState?.CombatEntityId ?? GameplayEntityId.None;

        private void Awake()
        {
            if (director == null) director = GetComponent<BossEncounterDirector>();
            if (bodyState == null) bodyState = GetComponent<BossBodyStateHost>();
            if (abilityDriver == null) abilityDriver = GetComponent<BossAbilityServerDriver>();
            if (statusHost == null) statusHost = GetComponent<CombatStatusHost>();
        }

        public override void OnNetworkDespawn()
        {
            m_LastSequenceByClientAttack.Clear();
            base.OnNetworkDespawn();
        }

        public bool SubmitTrustedHit(in TrustedCombatHit hit)
        {
            if (!IsSpawned) return false;
            if (IsServer)
            {
                ApplyHitStatuses(hit.Damage.Source, hit.Statuses);
                return ApplyServerHit(hit.Damage, ResolveAttackerPosition(hit.Damage.Source), out _);
            }
            RequestTrustedHitRpc(TrustedCombatHitNetworkMessage.FromDomain(hit));
            return true;
        }

        public bool TryApplyDamage(in DamageRequest request, out ResolvedDamage result)
        {
            result = default;
            if (!IsServer) return false;
            return ApplyServerHit(request, ResolveAttackerPosition(request.Source), out result);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestTrustedHitRpc(TrustedCombatHitNetworkMessage message, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            ulong replayKey = (sender << 32) ^ message.AttackId;
            if (m_LastSequenceByClientAttack.TryGetValue(replayKey, out ulong last) && message.Sequence <= last)
                return;
            m_LastSequenceByClientAttack[replayKey] = message.Sequence;

            var source = new GameplayEntityId(sender + 1UL);
            var request = new DamageRequest(source, CombatEntityId, message.AttackId,
                message.Sequence, message.Damage, (DamageTags)message.Tags);
            ApplyHitStatuses(source, message.Statuses.ToSpecs());
            ApplyServerHit(request, ResolveAttackerPosition(source), out _);
        }

        /// <summary>把命中携带的元素状态挂到 Boss 本体（经 <see cref="statusHost"/>，自动应用 elementResist 减免）。</summary>
        private void ApplyHitStatuses(GameplayEntityId source, StatusEffectSpec[] statuses)
        {
            if (statusHost == null || statuses == null || statuses.Length == 0) return;
            for (int i = 0; i < statuses.Length; i++)
                statusHost.TryApplyStatus(new StatusApplicationRequest(source, CombatEntityId, statuses[i]));
        }

        private bool ApplyServerHit(in DamageRequest incoming, in Float3 attackerPosition,
            out ResolvedDamage result)
        {
            result = default;
            if (!IsServer || director == null) return false;
            var validated = new DamageRequest(incoming.Source, CombatEntityId, incoming.AttackId,
                incoming.Sequence, incoming.BaseDamage, incoming.Tags);

            ServerCombatActivity.Interaction(NetworkManager, validated.Source, CombatEntityId, validated.AttackId, validated.Sequence);
            if ((validated.Tags & DamageTags.Parry) != 0)
            {
                bool cancelled = abilityDriver != null && abilityDriver.TryParryActiveAbilityServer();
                result = new ResolvedDamage(validated, 0f, validated.Tags, cancelled);
                return cancelled;
            }

            if (validated.BaseDamage <= 0f) return false;
            float guardBefore = director.GuardHealth;
            if (!director.TryApplyPlayerDamageServer(validated.Source, validated.BaseDamage,
                    attackerPosition, out BossDamageOutcome outcome)) return false;
            result = new ResolvedDamage(validated, validated.BaseDamage, validated.Tags, false);
            PublishBossResolution(result, guardBefore, director.GuardHealth, validated.BaseDamage);
            return outcome != BossDamageOutcome.Ignored;
        }

        /// <summary>
        /// 把玩家对 Boss 本体的命中补发到结算路由（<see cref="ServerCombatResolutionRouter"/>）。
        /// </summary>
        /// <remarks>
        /// <b>为什么需要</b>：玩家对 Boss 的伤害不走结算路由（只有 <c>EnemyNetworkActor</c> 与
        /// <c>PlayerCombatReceiver</c> 会 Publish），导致所有「玩家命中敌人时触发」的能力
        /// —— 撞击使魔、以及后续的无人机 / 魔符 / 命中类血契 —— 都收不到 Boss 的记录，
        /// 表现为<b>使魔打 Boss 时完全不出手</b>。这里补发后即可被正常登记为候选目标。
        /// </remarks>
        /// <remarks>
        /// <b>关于血量的口径</b>：Boss 没有单一血量（格挡条 + 阶段血两套），给不出真实的 before/after，
        /// 这里合成一对前后值，只保证两个派生字段的语义正确：
        /// ① <c>AppliedDamage</c> = 本击实际生效伤害 —— 路由器的投递门槛是 &gt; 0，为 0 会整条丢弃；
        /// ② <c>WasKilled</c> 恒为 false —— Boss 的死亡由 <c>BossEncounterDirector</c> 的阶段/败北流程负责，
        ///    若这里产出 WasKilled，监控插件的「每分钟击杀数」会把 Boss 也计进去，与既有统计口径不符。
        ///    （要让击杀 Boss 计入击杀数：把 baseline 在 <c>BossDamageOutcome.BossDefeated</c> 时改为 0。）
        /// </remarks>
        private void PublishBossResolution(in ResolvedDamage damage, float guardBefore, float guardAfter,
            float requestedDamage)
        {
            float applied = guardBefore - guardAfter;
            if (applied <= 0f)
            {
                // 处决窗口 / 破防后等不掉格挡条的阶段：退回按「请求伤害（含信任上限截断）」计。
                applied = director.Config != null
                    ? Mathf.Min(requestedDamage, director.Config.MaxTrustedHitDamage)
                    : requestedDamage;
            }
            if (applied <= 0f) return;

            const float baseline = 1f;   // 保证 TargetHealthAfter > 0，使 WasKilled 恒为 false
            var record = new CombatResolutionRecord(damage, applied + baseline, baseline);
            ServerCombatResolutionRouter.Publish(NetworkManager, record);
        }

        private Float3 ResolveAttackerPosition(GameplayEntityId source)
        {
            if (NetworkManager != null && !source.IsNone && source.Value > 0 &&
                NetworkManager.ConnectedClients.TryGetValue(source.Value - 1UL, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                Vector3 position = client.PlayerObject.transform.position;
                return new Float3(position.x, position.y, position.z);
            }
            Vector3 fallback = transform.position;
            return new Float3(fallback.x, fallback.y, fallback.z);
        }
    }
}
