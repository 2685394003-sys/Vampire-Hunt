using System;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Progression;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct PactDraftNetworkState : INetworkSerializable, IEquatable<PactDraftNetworkState>
    {
        public ulong OfferId;
        public uint Option0;
        public uint Option1;
        public uint Option2;
        public byte OptionCount;
        public byte RerollCount;
        public byte MaxRerolls;
        public int RerollCost;
        public float SelectionCost;

        public bool IsActive => OfferId != 0 && OptionCount > 0;

        public uint GetOption(int index)
        {
            if (index < 0 || index >= OptionCount) return 0;
            return index == 0 ? Option0 : index == 1 ? Option1 : Option2;
        }

        public bool Contains(uint pactId)
        {
            for (int i = 0; i < OptionCount; i++) if (GetOption(i) == pactId) return true;
            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OfferId);
            serializer.SerializeValue(ref Option0);
            serializer.SerializeValue(ref Option1);
            serializer.SerializeValue(ref Option2);
            serializer.SerializeValue(ref OptionCount);
            serializer.SerializeValue(ref RerollCount);
            serializer.SerializeValue(ref MaxRerolls);
            serializer.SerializeValue(ref RerollCost);
            serializer.SerializeValue(ref SelectionCost);
        }

        public bool Equals(PactDraftNetworkState other) =>
            OfferId == other.OfferId && Option0 == other.Option0 && Option1 == other.Option1 &&
            Option2 == other.Option2 && OptionCount == other.OptionCount &&
            RerollCount == other.RerollCount && MaxRerolls == other.MaxRerolls &&
            RerollCost == other.RerollCost && SelectionCost == other.SelectionCost;
    }

    /// <summary>Owner UI commands enter here; the server owns rolls, costs and inventory writes.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PactDraftNetworkBridge : NetworkBehaviour
    {
        [SerializeField] private PactCatalogAsset catalog;
        [SerializeField] private PactNetworkState pactState;
        [SerializeField] private CoreStatsHandler coreStats;
        [Tooltip("Raised locally when the owner presses the manual level-up button.")]
        [SerializeField] private GameEvent onTriggerLevelup;
        [Tooltip("Scarlet required for the first level up.")]
        [SerializeField, Min(0f)] private float selectionScarletCost = 100f;
        [Tooltip("Proportional cost increase after each successful level up. 0.1 means 10%.")]
        [SerializeField, Min(0f)] private float levelUpCostGrowthRate = 0.1f;
        [SerializeField, Min(0)] private int rerollScarletCost = 25;
        [SerializeField, Min(0)] private int maxRerolls = 1;
        [SerializeField, Range(1, 3)] private int optionCount = 3;
        [SerializeField] private int runSeed = 1337;

        private readonly NetworkVariable<PactDraftNetworkState> m_Draft =
            new NetworkVariable<PactDraftNetworkState>(default,
                NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> m_CompletedLevelUps =
            new NetworkVariable<int>(0,
                NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private readonly PactRollService m_RollService = new PactRollService();
        private PactCatalog m_DomainCatalog;
        private ulong m_NextOfferId = 1;
        private int m_RollIndex;
        private bool m_NoEligiblePacts;

        public event Action<PactDraftNetworkState> DraftChanged;
        public event Action<float> LevelUpCostChanged;
        public PactDraftNetworkState CurrentDraft => m_Draft.Value;
        public float SelectionScarletCost => LevelUpCostPolicy.CalculateRequiredScarlet(
            selectionScarletCost, levelUpCostGrowthRate, m_CompletedLevelUps.Value);

        private void Awake()
        {
            if (pactState == null) pactState = GetComponent<PactNetworkState>();
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_DomainCatalog = catalog != null ? catalog.CreateCatalog() : new PactCatalog(null);
            m_Draft.OnValueChanged += HandleDraftChanged;
            m_CompletedLevelUps.OnValueChanged += HandleCompletedLevelUpsChanged;
            if (IsServer) m_CompletedLevelUps.Value = pactState != null ? pactState.TotalStacks : 0;
            if (IsOwner)
            {
                onTriggerLevelup?.RegisterListener(HandleTriggerLevelup);
                DraftChanged?.Invoke(m_Draft.Value);
                LevelUpCostChanged?.Invoke(SelectionScarletCost);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner) onTriggerLevelup?.UnregisterListener(HandleTriggerLevelup);
            m_CompletedLevelUps.OnValueChanged -= HandleCompletedLevelUpsChanged;
            m_Draft.OnValueChanged -= HandleDraftChanged;
            base.OnNetworkDespawn();
        }

        private void HandleTriggerLevelup()
        {
            if (!IsSpawned || !IsOwner) return;
            RequestLevelUpRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestLevelUpRpc()
        {
            if (m_Draft.Value.IsActive || m_NoEligiblePacts || coreStats == null || pactState == null)
                return;

            float selectionCost = LevelUpCostPolicy.CalculateRequiredScarlet(
                selectionScarletCost, levelUpCostGrowthRate, m_CompletedLevelUps.Value);
            if (coreStats.GetCurrentValue(StatKeys.Scarlet) < selectionCost) return;
            CreateDraftServer(0, selectionCost);
        }

        public void SelectPact(uint pactId)
        {
            if (!IsOwner || !m_Draft.Value.IsActive) return;
            SelectPactRpc(m_Draft.Value.OfferId, pactId);
        }

        public void Reroll()
        {
            if (!IsOwner || !m_Draft.Value.IsActive) return;
            RerollRpc(m_Draft.Value.OfferId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SelectPactRpc(ulong offerId, uint pactId)
        {
            PactDraftNetworkState draft = m_Draft.Value;
            if (!draft.IsActive || draft.OfferId != offerId || !draft.Contains(pactId) ||
                pactState == null || coreStats == null ||
                !m_DomainCatalog.TryGet(pactId, out PactDefinition definition)) return;

            PactInventory inventory = pactState.CreateInventorySnapshot();
            float remainingSelectionCost = Mathf.Max(
                0f, draft.SelectionCost - draft.RerollCount * draft.RerollCost);
            if (!m_RollService.IsEligible(m_DomainCatalog, inventory, definition) ||
                !coreStats.TryConsumeStat(StatKeys.Scarlet, remainingSelectionCost, OwnerClientId)) return;

            if (!pactState.TryAddOrStackServer(pactId, out _))
            {
                coreStats.ModifyStat(StatKeys.Scarlet, remainingSelectionCost, OwnerClientId,
                    ModificationSource.Direct);
                return;
            }

            m_CompletedLevelUps.Value = Mathf.Max(m_CompletedLevelUps.Value + 1, pactState.TotalStacks);
            m_Draft.Value = default;
            m_NoEligiblePacts = false;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RerollRpc(ulong offerId)
        {
            PactDraftNetworkState draft = m_Draft.Value;
            if (!draft.IsActive || draft.OfferId != offerId || draft.RerollCount >= draft.MaxRerolls ||
                coreStats == null || !coreStats.TryConsumeStat(
                    StatKeys.Scarlet, draft.RerollCost, OwnerClientId)) return;
            CreateDraftServer(draft.RerollCount + 1, draft.SelectionCost);
        }

        private void CreateDraftServer(int rerollCount, float selectionCost)
        {
            PactInventory inventory = pactState != null ? pactState.CreateInventorySnapshot() : new PactInventory();
            int seed = unchecked(runSeed * 397 ^ (int)OwnerClientId * 7919 ^ ++m_RollIndex * 104729);
            uint[] options = m_RollService.Roll(m_DomainCatalog, inventory, optionCount, seed);
            if (options.Length == 0)
            {
                m_NoEligiblePacts = true;
                m_Draft.Value = default;
                return;
            }

            m_Draft.Value = new PactDraftNetworkState
            {
                OfferId = m_NextOfferId++,
                Option0 = options.Length > 0 ? options[0] : 0,
                Option1 = options.Length > 1 ? options[1] : 0,
                Option2 = options.Length > 2 ? options[2] : 0,
                OptionCount = (byte)options.Length,
                RerollCount = (byte)rerollCount,
                MaxRerolls = (byte)Mathf.Clamp(maxRerolls, 0, byte.MaxValue),
                RerollCost = rerollScarletCost,
                SelectionCost = selectionCost
            };
        }

        private void HandleDraftChanged(PactDraftNetworkState previous, PactDraftNetworkState current)
        {
            if (IsOwner) DraftChanged?.Invoke(current);
        }

        private void HandleCompletedLevelUpsChanged(int previous, int current)
        {
            if (IsOwner) LevelUpCostChanged?.Invoke(SelectionScarletCost);
        }
    }
}
