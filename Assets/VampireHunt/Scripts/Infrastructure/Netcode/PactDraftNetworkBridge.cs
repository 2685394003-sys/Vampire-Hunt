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
        [Header("等级上限")]
        [Tooltip("玩家等级上限。等级 = 1 + 已完成升级次数（= 已装血契数）；达到上限后按 Z 不再触发升级面板。")]
        [SerializeField, Min(1)] private int maxLevel = 20;
        [Tooltip("固定局种子：仅当 autoSeedPerRun=false 时作为回退值使用（复现/回归）。")]
        [SerializeField] private int runSeed = 1337;
        [Tooltip("每局开局由 server 自动生成随机局种子，替代固定 1337（修复每局第一次抽卡/整局随机全部可复现的问题）。关闭 = 回退到上方固定 runSeed，用于复现指定局。")]
        [SerializeField] private bool autoSeedPerRun = true;

        [Header("解锁契软保底（soft pity）")]
        [Tooltip("仍可获得（未拥有、前置满足、互斥不冲突）的形态解锁契名单：武器/领域/使魔获得契。")]
        [SerializeField] private uint[] pityUnlockPactIds = { 2001, 3001, 4001, 5001, 6001, 7001, 8001 };
        [Tooltip("阈值 N：连续 N 次「新的升级」初始选项未刷出任何仍合格的解锁契 → 下次升级触发（权重 ×K）。reroll 不推进也不清零。")]
        [SerializeField, Min(1)] private int pityMissThreshold = 3;
        [Tooltip("力度 K：触发后解锁契权重乘子（仍是权重采样，非硬塞必出）。触发轮仍空手则计数继续累计、保底延续。")]
        [SerializeField, Min(1f)] private float pityWeightMultiplier = 4f;

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
        private int m_RunSeed = 1337;
        private bool m_NoEligibleOptions;
        private int m_UnlockMissStreak;

        public event Action<PactDraftNetworkState> DraftChanged;
        public event Action<float> LevelUpCostChanged;
        public PactDraftNetworkState CurrentDraft => m_Draft.Value;
        public float SelectionScarletCost => LevelUpCostPolicy.CalculateRequiredScarlet(
            selectionScarletCost, levelUpCostGrowthRate, m_CompletedLevelUps.Value);
        /// <summary>当前等级 = 1 + 已完成升级次数（与已装血契数一致）。</summary>
        public int CurrentLevel => m_CompletedLevelUps.Value + 1;
        /// <summary>是否已达到等级上限：达到后不再触发升级面板（升级成本提示可据此隐藏）。</summary>
        public bool IsLevelMaxed => m_CompletedLevelUps.Value >= Mathf.Max(0, maxLevel - 1);

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
            if (IsServer)
            {
                m_RunSeed = autoSeedPerRun ? Guid.NewGuid().GetHashCode() : runSeed;
                m_CompletedLevelUps.Value = pactState != null ? pactState.TotalStacks : 0;
                m_UnlockMissStreak = 0;
            }
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
            // 等级上限：等级 = 1 + 已完成升级次数，达到 maxLevel 后不再开升级面板。
            if (IsLevelMaxed) return;

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
            int pactSeed = unchecked(m_RunSeed * 397 ^ (int)OwnerClientId * 7919 ^ roll * 104729);
            int affixSeed = unchecked(pactSeed ^ (int)0x5F356495);

            // 解锁契软保底：连续 N 次「新升级初始选项」都没出现仍可获得的解锁契 → 下次升级权重 ×K。
            // 仅当名单内还有可获得的解锁契时才激活（避免已全拿到/被武器线互斥锁死后空转）。
            bool hasEligibleUnlock = HasEligibleUnlockPity(inventory);
            bool pityActive = hasEligibleUnlock && m_UnlockMissStreak >= pityMissThreshold;
            uint[] pactOptions = m_PactRollService.Roll(
                m_DomainCatalog, inventory, optionCount, pactSeed,
                pityUnlockIds: pityUnlockPactIds, pityActive: pityActive,
                pityWeightMultiplier: pityWeightMultiplier);
            uint[] affixOptions = m_AffixRollService.Roll(
                m_DomainAffixCatalog, affixSet, optionCount, affixSeed);
            if (pactOptions.Length == 0 || affixOptions.Length == 0)
            {
                m_NoEligibleOptions = true;
                m_Draft.Value = default;
                return;
            }

            // 更新 miss 计数：见着解锁契→清零；新升级(非 reroll)未见→+1；reroll 未见→不变(同一升级不双计)；
            // 名单已无可获得解锁契→归零(保底无对象，不累计)。
            if (!hasEligibleUnlock)
            {
                m_UnlockMissStreak = 0;
            }
            else if (OptionsContainPityUnlock(pactOptions))
            {
                m_UnlockMissStreak = 0;
            }
            else if (rerollCount == 0)
            {
                m_UnlockMissStreak++;
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

        /// <summary>名单内是否还有「仍可获得」的解锁契（未拥有、前置满足、互斥不冲突）。无对象时保底不激活也不累计。</summary>
        private bool HasEligibleUnlockPity(PactInventory inventory)
        {
            if (inventory == null || pityUnlockPactIds == null || pityUnlockPactIds.Length == 0)
                return false;
            for (int i = 0; i < pityUnlockPactIds.Length; i++)
            {
                if (!m_DomainCatalog.TryGet(pityUnlockPactIds[i], out PactDefinition definition))
                    continue;
                if (m_PactRollService.IsEligible(m_DomainCatalog, inventory, definition))
                    return true;
            }
            return false;
        }

        /// <summary>本次 roll 选项里是否出现了名单内的解锁契（roll 结果必为 eligible，故命中即视为「本次见过解锁契」）。</summary>
        private bool OptionsContainPityUnlock(uint[] pactOptions)
        {
            if (pactOptions == null || pityUnlockPactIds == null) return false;
            for (int o = 0; o < pactOptions.Length; o++)
            {
                for (int i = 0; i < pityUnlockPactIds.Length; i++)
                {
                    if (pactOptions[o] == pityUnlockPactIds[i]) return true;
                }
            }
            return false;
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
