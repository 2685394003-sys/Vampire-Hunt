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
        public uint AffixOption0;
        public uint AffixOption1;
        public uint AffixOption2;
        public byte AffixOptionCount;
        public byte RerollCount;
        public byte MaxRerolls;
        public int RerollCost;
        public float SelectionCost;

        public bool IsActive => OfferId != 0 && OptionCount > 0 && AffixOptionCount > 0;

        public uint GetOption(int index)
        {
            if (index < 0 || index >= OptionCount) return 0;
            return index == 0 ? Option0 : index == 1 ? Option1 : Option2;
        }

        public uint GetAffixOption(int index)
        {
            if (index < 0 || index >= AffixOptionCount) return 0;
            return index == 0 ? AffixOption0 : index == 1 ? AffixOption1 : AffixOption2;
        }

        public bool ContainsPact(uint pactId)
        {
            for (int i = 0; i < OptionCount; i++) if (GetOption(i) == pactId) return true;
            return false;
        }

        public bool ContainsAffix(uint affixId)
        {
            for (int i = 0; i < AffixOptionCount; i++)
                if (GetAffixOption(i) == affixId) return true;
            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OfferId);
            serializer.SerializeValue(ref Option0);
            serializer.SerializeValue(ref Option1);
            serializer.SerializeValue(ref Option2);
            serializer.SerializeValue(ref OptionCount);
            serializer.SerializeValue(ref AffixOption0);
            serializer.SerializeValue(ref AffixOption1);
            serializer.SerializeValue(ref AffixOption2);
            serializer.SerializeValue(ref AffixOptionCount);
            serializer.SerializeValue(ref RerollCount);
            serializer.SerializeValue(ref MaxRerolls);
            serializer.SerializeValue(ref RerollCost);
            serializer.SerializeValue(ref SelectionCost);
        }

        public bool Equals(PactDraftNetworkState other) =>
            OfferId == other.OfferId && Option0 == other.Option0 && Option1 == other.Option1 &&
            Option2 == other.Option2 && OptionCount == other.OptionCount &&
            AffixOption0 == other.AffixOption0 && AffixOption1 == other.AffixOption1 &&
            AffixOption2 == other.AffixOption2 && AffixOptionCount == other.AffixOptionCount &&
            RerollCount == other.RerollCount && MaxRerolls == other.MaxRerolls &&
            RerollCost == other.RerollCost && SelectionCost == other.SelectionCost;
    }

    /// <summary>
    /// Owner UI commands enter here. The server rolls both rows and commits one player pact plus
    /// one run-wide enemy affix as a single level-up choice.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PactDraftNetworkBridge : NetworkBehaviour
    {
        [SerializeField] private PactCatalogAsset catalog;
        [SerializeField] private EnemyAffixCatalogAsset enemyAffixCatalog;
        [SerializeField] private PactNetworkState pactState;
        [SerializeField] private EnemyAffixRunState enemyAffixState;
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
        private readonly PactRollService m_PactRollService = new PactRollService();
        private readonly EnemyAffixRollService m_AffixRollService = new EnemyAffixRollService();
        private PactCatalog m_DomainCatalog;
        private EnemyAffixCatalog m_DomainAffixCatalog;
        private ulong m_NextOfferId = 1;
        private int m_RollIndex;
        private bool m_NoEligibleOptions;

        public event Action<PactDraftNetworkState> DraftChanged;
        public event Action<float> LevelUpCostChanged;
        public PactDraftNetworkState CurrentDraft => m_Draft.Value;
        public float SelectionScarletCost => LevelUpCostPolicy.CalculateRequiredScarlet(
            selectionScarletCost, levelUpCostGrowthRate, m_CompletedLevelUps.Value);

        private void Awake()
        {
            if (pactState == null) pactState = GetComponent<PactNetworkState>();
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
            ResolveEnemyAffixDependencies();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResolveEnemyAffixDependencies();
            m_DomainCatalog = catalog != null ? catalog.CreateCatalog() : new PactCatalog(null);
            m_DomainAffixCatalog = enemyAffixCatalog != null
                ? enemyAffixCatalog.CreateCatalog()
                : new EnemyAffixCatalog(null);
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
            ResolveEnemyAffixDependencies();
            if (m_Draft.Value.IsActive || m_NoEligibleOptions || coreStats == null ||
                pactState == null || enemyAffixState == null) return;

            float selectionCost = LevelUpCostPolicy.CalculateRequiredScarlet(
                selectionScarletCost, levelUpCostGrowthRate, m_CompletedLevelUps.Value);
            if (coreStats.GetCurrentValue(StatKeys.Scarlet) < selectionCost) return;
            CreateDraftServer(0, selectionCost);
        }

        public void ConfirmSelection(uint pactId, uint affixId)
        {
            if (!IsOwner || !m_Draft.Value.IsActive || pactId == 0 || affixId == 0) return;
            ConfirmSelectionRpc(m_Draft.Value.OfferId, pactId, affixId);
        }

        public void Reroll()
        {
            if (!IsOwner || !m_Draft.Value.IsActive) return;
            RerollRpc(m_Draft.Value.OfferId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ConfirmSelectionRpc(ulong offerId, uint pactId, uint affixId)
        {
            ResolveEnemyAffixDependencies();
            PactDraftNetworkState draft = m_Draft.Value;
            if (!draft.IsActive || draft.OfferId != offerId ||
                !draft.ContainsPact(pactId) || !draft.ContainsAffix(affixId) ||
                pactState == null || enemyAffixState == null || coreStats == null ||
                !m_DomainCatalog.TryGet(pactId, out PactDefinition pactDefinition) ||
                !m_DomainAffixCatalog.TryGet(affixId, out EnemyAffixDefinition affixDefinition)) return;

            PactInventory inventory = pactState.CreateInventorySnapshot();
            EnemyAffixSet affixSet = enemyAffixState.CreateSetSnapshot();
            float remainingSelectionCost = Mathf.Max(
                0f, draft.SelectionCost - draft.RerollCount * draft.RerollCost);
            if (!m_PactRollService.IsEligible(m_DomainCatalog, inventory, pactDefinition) ||
                !m_AffixRollService.IsEligible(m_DomainAffixCatalog, affixSet, affixDefinition) ||
                !coreStats.TryConsumeStat(
                    StatKeys.Scarlet,
                    remainingSelectionCost,
                    OwnerClientId)) return;

            if (!enemyAffixState.TryAddOrStackServer(affixId, out _))
            {
                RefundSelectionCost(remainingSelectionCost);
                return;
            }

            if (!pactState.TryAddOrStackServer(pactId, out _))
            {
                enemyAffixState.TryRemoveOneServer(affixId);
                RefundSelectionCost(remainingSelectionCost);
                return;
            }

            m_CompletedLevelUps.Value = Mathf.Max(m_CompletedLevelUps.Value + 1, pactState.TotalStacks);
            m_Draft.Value = default;
            m_NoEligibleOptions = false;
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
            ResolveEnemyAffixDependencies();
            if (enemyAffixState == null)
            {
                m_Draft.Value = default;
                return;
            }

            PactInventory inventory = pactState != null
                ? pactState.CreateInventorySnapshot()
                : new PactInventory();
            EnemyAffixSet affixSet = enemyAffixState.CreateSetSnapshot();
            int roll = ++m_RollIndex;
            int pactSeed = unchecked(runSeed * 397 ^ (int)OwnerClientId * 7919 ^ roll * 104729);
            int affixSeed = unchecked(pactSeed ^ (int)0x5F356495);
            uint[] pactOptions = m_PactRollService.Roll(
                m_DomainCatalog, inventory, optionCount, pactSeed);
            uint[] affixOptions = m_AffixRollService.Roll(
                m_DomainAffixCatalog, affixSet, optionCount, affixSeed);
            if (pactOptions.Length == 0 || affixOptions.Length == 0)
            {
                m_NoEligibleOptions = true;
                m_Draft.Value = default;
                return;
            }

            m_Draft.Value = new PactDraftNetworkState
            {
                OfferId = m_NextOfferId++,
                Option0 = pactOptions.Length > 0 ? pactOptions[0] : 0,
                Option1 = pactOptions.Length > 1 ? pactOptions[1] : 0,
                Option2 = pactOptions.Length > 2 ? pactOptions[2] : 0,
                OptionCount = (byte)pactOptions.Length,
                AffixOption0 = affixOptions.Length > 0 ? affixOptions[0] : 0,
                AffixOption1 = affixOptions.Length > 1 ? affixOptions[1] : 0,
                AffixOption2 = affixOptions.Length > 2 ? affixOptions[2] : 0,
                AffixOptionCount = (byte)affixOptions.Length,
                RerollCount = (byte)rerollCount,
                MaxRerolls = (byte)Mathf.Clamp(maxRerolls, 0, byte.MaxValue),
                RerollCost = rerollScarletCost,
                SelectionCost = selectionCost
            };
        }

        private void ResolveEnemyAffixDependencies()
        {
            if (enemyAffixState == null) enemyAffixState = FindAnyObjectByType<EnemyAffixRunState>();
            if (enemyAffixCatalog == null && enemyAffixState != null)
                enemyAffixCatalog = enemyAffixState.CatalogAsset;
        }

        private void RefundSelectionCost(float amount)
        {
            coreStats.ModifyStat(
                StatKeys.Scarlet,
                amount,
                OwnerClientId,
                ModificationSource.Direct);
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
