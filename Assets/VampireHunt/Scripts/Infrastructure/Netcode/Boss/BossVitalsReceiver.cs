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

        private readonly Dictionary<ulong, ulong> m_LastSequenceByClientAttack =
            new Dictionary<ulong, ulong>();

        public GameplayEntityId CombatEntityId => bodyState?.CombatEntityId ?? GameplayEntityId.None;

        private void Awake()
        {
            if (director == null) director = GetComponent<BossEncounterDirector>();
            if (bodyState == null) bodyState = GetComponent<BossBodyStateHost>();
        }

        public override void OnNetworkDespawn()
        {
            m_LastSequenceByClientAttack.Clear();
            base.OnNetworkDespawn();
        }

        public bool SubmitTrustedHit(in TrustedCombatHit hit)
        {
            if (!IsSpawned) return false;
            if (IsServer) return ApplyServerHit(hit.Damage, ResolveAttackerPosition(hit.Damage.Source), out _);
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
            ApplyServerHit(request, ResolveAttackerPosition(source), out _);
        }

        private bool ApplyServerHit(in DamageRequest incoming, in Float3 attackerPosition,
            out ResolvedDamage result)
        {
            result = default;
            if (!IsServer || director == null || incoming.BaseDamage <= 0f) return false;
            var validated = new DamageRequest(incoming.Source, CombatEntityId, incoming.AttackId,
                incoming.Sequence, incoming.BaseDamage, incoming.Tags);
            if (!director.TryApplyPlayerDamageServer(validated.Source, validated.BaseDamage,
                    attackerPosition, out BossDamageOutcome outcome)) return false;
            result = new ResolvedDamage(validated, validated.BaseDamage, validated.Tags, false);
            return outcome != BossDamageOutcome.Ignored;
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
