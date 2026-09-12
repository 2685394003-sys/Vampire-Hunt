using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Presentation.HUD
{
    /// <summary>Owner-only UI adapter for selecting one player pact and one enemy affix.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PactSelectionPresenter : NetworkBehaviour
    {
        private const int OptionCount = 3;

        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PactDraftNetworkBridge draftBridge;
        [SerializeField] private PactNetworkState pactState;
        [SerializeField] private PactCatalogAsset catalog;
        [SerializeField] private EnemyAffixCatalogAsset enemyAffixCatalog;
        [SerializeField] private EnemyAffixRunState enemyAffixState;
        [SerializeField] private VampireHuntHudPresenter hud;

        private readonly List<PactStackNetworkState> m_Pacts = new List<PactStackNetworkState>();
        private readonly VisualElement[] m_PactCards = new VisualElement[OptionCount];
        private readonly Button[] m_PactButtons = new Button[OptionCount];
        private readonly Action[] m_PactCallbacks = new Action[OptionCount];
        private readonly Label[] m_PactNames = new Label[OptionCount];
        private readonly Label[] m_PactDescriptions = new Label[OptionCount];
        private readonly Label[] m_PactStacks = new Label[OptionCount];
        private readonly VisualElement[] m_AffixCards = new VisualElement[OptionCount];
        private readonly Button[] m_AffixButtons = new Button[OptionCount];
        private readonly Action[] m_AffixCallbacks = new Action[OptionCount];
        private readonly Label[] m_AffixNames = new Label[OptionCount];
        private readonly Label[] m_AffixDescriptions = new Label[OptionCount];
        private readonly Label[] m_AffixStacks = new Label[OptionCount];
        private VisualElement m_Overlay;
        private Label m_DraftSubtitle;
        private Label m_LevelUpHint;
        private Label m_SelectionSummary;
        private readonly VisualElement[] m_PactIcons = new VisualElement[OptionCount];
        private readonly VisualElement[] m_AffixIcons = new VisualElement[OptionCount];
        private Button m_RerollButton;
        private Label m_RerollHint;
        private Button m_ConfirmButton;
        private bool m_Bound;
        private bool m_CursorCaptured;
        private CursorLockMode m_PreviousLockMode;
        private bool m_PreviousCursorVisible;
        private ulong m_RenderedOfferId;
        private uint m_SelectedPactId;
        private uint m_SelectedAffixId;
        private bool m_PactMenuPausedByUs;

        private void Awake()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (draftBridge == null) draftBridge = GetComponent<PactDraftNetworkBridge>();
            if (pactState == null) pactState = GetComponent<PactNetworkState>();
            if (hud == null) hud = GetComponent<VampireHuntHudPresenter>();
            ResolveEnemyAffixDependencies();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsOwner) return;
            ResolveEnemyAffixDependencies();
            if (draftBridge != null)
            {
                draftBridge.DraftChanged += RenderDraft;
                draftBridge.LevelUpCostChanged += RenderLevelUpCost;
            }
            if (pactState != null) pactState.InventoryChanged += RenderSummary;
            if (enemyAffixState != null) enemyAffixState.AffixesChanged += HandleAffixesChanged;
            StartCoroutine(BindNextFrame());
        }

        public override void OnNetworkDespawn()
        {
            if (draftBridge != null)
            {
                draftBridge.DraftChanged -= RenderDraft;
                draftBridge.LevelUpCostChanged -= RenderLevelUpCost;
            }
            if (pactState != null) pactState.InventoryChanged -= RenderSummary;
            if (enemyAffixState != null) enemyAffixState.AffixesChanged -= HandleAffixesChanged;
            UnbindButtons();
            SetCursorForDraft(false);
            if (m_PactMenuPausedByUs)
            {
                m_PactMenuPausedByUs = false;
                MenuPauseController.ReleasePactPause();
            }
            base.OnNetworkDespawn();
        }

        private IEnumerator BindNextFrame()
        {
            yield return null;
            BindUi();
            RenderDraft(draftBridge != null ? draftBridge.CurrentDraft : default);
            RenderLevelUpCost(draftBridge != null ? draftBridge.SelectionScarletCost : 0f);
            RenderSummary();
        }

        private void BindUi()
        {
            if (m_Bound || uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;
            m_Overlay = root.Q<VisualElement>("pact-selection-overlay");
            m_DraftSubtitle = root.Q<Label>("pact-draft-subtitle");
            m_LevelUpHint = root.Q<Label>("level-up-hint");
            m_SelectionSummary = root.Q<Label>("pact-selection-summary");
            for (int i = 0; i < OptionCount; i++)
            {
                int capturedIndex = i;
                m_PactCards[i] = root.Q<VisualElement>($"pact-card-{i}");
                m_PactButtons[i] = root.Q<Button>($"pact-select-{i}");
                m_PactNames[i] = root.Q<Label>($"pact-name-{i}");
                m_PactDescriptions[i] = root.Q<Label>($"pact-description-{i}");
                m_PactStacks[i] = root.Q<Label>($"pact-stack-{i}");
                m_PactIcons[i] = root.Q<VisualElement>($"pact-icon-{i}");
                if (m_PactButtons[i] != null)
                {
                    m_PactCallbacks[i] = () => SelectPactOption(capturedIndex);
                    m_PactButtons[i].clicked += m_PactCallbacks[i];
                }

                m_AffixCards[i] = root.Q<VisualElement>($"affix-card-{i}");
                m_AffixButtons[i] = root.Q<Button>($"affix-select-{i}");
                m_AffixNames[i] = root.Q<Label>($"affix-name-{i}");
                m_AffixDescriptions[i] = root.Q<Label>($"affix-description-{i}");
                m_AffixStacks[i] = root.Q<Label>($"affix-stack-{i}");
                m_AffixIcons[i] = root.Q<VisualElement>($"affix-icon-{i}");
                if (m_AffixButtons[i] != null)
                {
                    m_AffixCallbacks[i] = () => SelectAffixOption(capturedIndex);
                    m_AffixButtons[i].clicked += m_AffixCallbacks[i];
                }
            }
            m_RerollButton = root.Q<Button>("pact-reroll");
            m_RerollHint = root.Q<Label>("pact-reroll-hint");
            m_ConfirmButton = root.Q<Button>("pact-confirm");
            if (m_RerollButton != null) m_RerollButton.clicked += Reroll;
            if (m_ConfirmButton != null) m_ConfirmButton.clicked += ConfirmSelection;
            m_Bound = true;
        }

        private void UnbindButtons()
        {
            for (int i = 0; i < OptionCount; i++)
            {
                if (m_PactButtons[i] != null && m_PactCallbacks[i] != null)
                    m_PactButtons[i].clicked -= m_PactCallbacks[i];
                if (m_AffixButtons[i] != null && m_AffixCallbacks[i] != null)
                    m_AffixButtons[i].clicked -= m_AffixCallbacks[i];
                m_PactCallbacks[i] = null;
                m_AffixCallbacks[i] = null;
            }
            if (m_RerollButton != null) m_RerollButton.clicked -= Reroll;
            if (m_ConfirmButton != null) m_ConfirmButton.clicked -= ConfirmSelection;
            m_Bound = false;
        }

        private void RenderDraft(PactDraftNetworkState draft)
        {
            BindUi();
            if (m_Overlay == null) return;
            bool newlyOpened = draft.IsActive && m_RenderedOfferId == 0;
            if (m_RenderedOfferId != draft.OfferId)
            {
                m_RenderedOfferId = draft.OfferId;
                m_SelectedPactId = 0;
                m_SelectedAffixId = 0;
            }

            m_Overlay.style.display = draft.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
            SetCursorForDraft(draft.IsActive);

            // 单人模式下，打开血契菜单时暂停游戏，关闭时恢复
            if (draft.IsActive && !m_PactMenuPausedByUs)
            {
                m_PactMenuPausedByUs = true;
                MenuPauseController.RequestPactPause();
            }
            else if (!draft.IsActive && m_PactMenuPausedByUs)
            {
                m_PactMenuPausedByUs = false;
                MenuPauseController.ReleasePactPause();
            }
            if (m_DraftSubtitle != null)
                m_DraftSubtitle.text = draft.IsActive
                    ? $"选择一份血契和一份副契，确认后消耗 {Mathf.CeilToInt(draft.SelectionCost)} 猩红"
                    : string.Empty;

            RenderPactOptions(draft);
            RenderAffixOptions(draft);
            RenderSelectionState(draft);
            if (newlyOpened) m_PactButtons[0]?.Focus();

            if (m_RerollButton != null)
            {
                bool canReroll = draft.IsActive && draft.RerollCount < draft.MaxRerolls;
                m_RerollButton.SetEnabled(canReroll);
            }
            if (m_RerollHint != null)
                m_RerollHint.text = draft.IsActive
                    ? draft.RerollCount < draft.MaxRerolls
                        ? $"刷新两排选项，预付 {draft.RerollCost} 猩红，总消耗仍为 {Mathf.CeilToInt(draft.SelectionCost)}"
                        : "本次选择已刷新"
                    : string.Empty;
        }

        private void RenderPactOptions(PactDraftNetworkState draft)
        {
            for (int i = 0; i < OptionCount; i++)
            {
                bool available = i < draft.OptionCount;
                if (m_PactCards[i] != null)
                    m_PactCards[i].style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                if (!available) continue;

                uint pactId = draft.GetOption(i);
                if (m_PactIcons[i] != null)
                {
                    m_PactIcons[i].style.backgroundImage = StyleKeyword.Null;
                    m_PactIcons[i].EnableInClassList("pact-art--blade", pactId >= 1001 && pactId <= 1005);
                }
                int currentStacks = pactState != null ? pactState.GetStacks(pactId) : 0;
                if (catalog != null && catalog.TryGetAsset(pactId, out PactDefinitionAsset asset))
                {
                    if (m_PactNames[i] != null) m_PactNames[i].text = asset.DisplayName;
                    if (m_PactDescriptions[i] != null) m_PactDescriptions[i].text = asset.Description;
                    if (m_PactIcons[i] != null && asset.Icon != null)
                        m_PactIcons[i].style.backgroundImage = new StyleBackground(asset.Icon);
                }
                else
                {
                    if (m_PactNames[i] != null) m_PactNames[i].text = $"血契 {pactId}";
                    if (m_PactDescriptions[i] != null) m_PactDescriptions[i].text = string.Empty;
                }
                if (m_PactStacks[i] != null)
                    m_PactStacks[i].text = currentStacks > 0
                        ? $"自身：{currentStacks} → {currentStacks + 1} 层"
                        : "自身：尚未拥有";
            }
        }

        private void RenderAffixOptions(PactDraftNetworkState draft)
        {
            ResolveEnemyAffixDependencies();
            for (int i = 0; i < OptionCount; i++)
            {
                bool available = i < draft.AffixOptionCount;
                if (m_AffixCards[i] != null)
                    m_AffixCards[i].style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                if (!available) continue;

                uint affixId = draft.GetAffixOption(i);
                if (m_AffixIcons[i] != null) m_AffixIcons[i].style.backgroundImage = StyleKeyword.Null;
                int currentStacks = enemyAffixState != null ? enemyAffixState.GetStacks(affixId) : 0;
                if (enemyAffixCatalog != null &&
                    enemyAffixCatalog.TryGetAsset(affixId, out EnemyAffixDefinitionAsset asset))
                {
                    if (m_AffixNames[i] != null) m_AffixNames[i].text = asset.DisplayName;
                    if (m_AffixDescriptions[i] != null) m_AffixDescriptions[i].text = asset.Description;
                    if (m_AffixIcons[i] != null && asset.Icon != null)
                        m_AffixIcons[i].style.backgroundImage = new StyleBackground(asset.Icon);
                }
                else
                {
                    if (m_AffixNames[i] != null) m_AffixNames[i].text = $"副契 {affixId}";
                    if (m_AffixDescriptions[i] != null) m_AffixDescriptions[i].text = string.Empty;
                }
                if (m_AffixStacks[i] != null)
                    m_AffixStacks[i].text = currentStacks > 0
                        ? $"敌群：{currentStacks} → {currentStacks + 1} 层"
                        : "敌群：尚未生效";
            }
        }

        private void RenderSelectionState(PactDraftNetworkState draft)
        {
            string pactName = "未选择";
            string affixName = "未选择";
            for (int i = 0; i < OptionCount; i++)
            {
                bool pactSelected = draft.GetOption(i) != 0 && draft.GetOption(i) == m_SelectedPactId;
                bool affixSelected = draft.GetAffixOption(i) != 0 &&
                                     draft.GetAffixOption(i) == m_SelectedAffixId;
                m_PactCards[i]?.EnableInClassList("draft-card--selected", pactSelected);
                m_AffixCards[i]?.EnableInClassList("draft-card--selected", affixSelected);
                if (m_PactButtons[i] != null) m_PactButtons[i].text = pactSelected ? "已选择" : "选择";
                if (m_AffixButtons[i] != null) m_AffixButtons[i].text = affixSelected ? "已选择" : "选择";
                if (pactSelected) pactName = m_PactNames[i]?.text ?? "已选择";
                if (affixSelected) affixName = m_AffixNames[i]?.text ?? "已选择";
            }
            if (m_SelectionSummary != null)
                m_SelectionSummary.text = $"血契：{pactName}    /    副契：{affixName}";
            if (m_ConfirmButton != null)
                m_ConfirmButton.SetEnabled(
                    draft.IsActive && m_SelectedPactId != 0 && m_SelectedAffixId != 0);
        }

        private void RenderSummary()
        {
            if (hud == null || pactState == null) return;
            pactState.Capture(m_Pacts);
            var names = new StringBuilder();
            for (int i = 0; i < m_Pacts.Count && i < 2; i++)
            {
                if (i > 0) names.Append(" · ");
                if (catalog != null && catalog.TryGetAsset(m_Pacts[i].PactId, out PactDefinitionAsset asset))
                    names.Append(asset.DisplayName);
                else names.Append('#').Append(m_Pacts[i].PactId);
                if (m_Pacts[i].Stacks > 1) names.Append('×').Append(m_Pacts[i].Stacks);
            }
            if (m_Pacts.Count > 2) names.Append(" …");
            hud.SetPactSummary(pactState.TotalStacks, names.ToString());
        }

        private void RenderLevelUpCost(float cost)
        {
            BindUi();
            if (m_LevelUpHint != null)
                m_LevelUpHint.text = draftBridge != null && draftBridge.IsLevelMaxed
                    ? "已达等级上限"
                    : $"Z  缔结血契 · 需要 {Mathf.CeilToInt(cost)} 猩红";
            hud?.SetUpgradeCost(cost, draftBridge != null && draftBridge.IsLevelMaxed);
        }

        private void SelectPactOption(int index)
        {
            if (draftBridge == null) return;
            uint pactId = draftBridge.CurrentDraft.GetOption(index);
            if (pactId == 0) return;
            m_SelectedPactId = pactId;
            RenderSelectionState(draftBridge.CurrentDraft);
        }

        private void SelectAffixOption(int index)
        {
            if (draftBridge == null) return;
            uint affixId = draftBridge.CurrentDraft.GetAffixOption(index);
            if (affixId == 0) return;
            m_SelectedAffixId = affixId;
            RenderSelectionState(draftBridge.CurrentDraft);
        }

        private void ConfirmSelection()
        {
            if (draftBridge == null || m_SelectedPactId == 0 || m_SelectedAffixId == 0) return;
            draftBridge.ConfirmSelection(m_SelectedPactId, m_SelectedAffixId);
        }

        private void Reroll() => draftBridge?.Reroll();

        private void HandleAffixesChanged()
        {
            if (draftBridge != null) RenderDraft(draftBridge.CurrentDraft);
        }

        private void ResolveEnemyAffixDependencies()
        {
            if (enemyAffixState == null) enemyAffixState = FindAnyObjectByType<EnemyAffixRunState>();
            if (enemyAffixCatalog == null && enemyAffixState != null)
                enemyAffixCatalog = enemyAffixState.CatalogAsset;
        }

        private void SetCursorForDraft(bool active)
        {
            if (active && !m_CursorCaptured)
            {
                m_PreviousLockMode = UnityEngine.Cursor.lockState;
                m_PreviousCursorVisible = UnityEngine.Cursor.visible;
                m_CursorCaptured = true;
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            else if (!active && m_CursorCaptured)
            {
                UnityEngine.Cursor.lockState = m_PreviousLockMode;
                UnityEngine.Cursor.visible = m_PreviousCursorVisible;
                m_CursorCaptured = false;
            }
        }
    }
}
