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
    /// <summary>Owner-only UI adapter. It displays server offers and sends only option IDs back.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PactSelectionPresenter : NetworkBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PactDraftNetworkBridge draftBridge;
        [SerializeField] private PactNetworkState pactState;
        [SerializeField] private PactCatalogAsset catalog;
        [SerializeField] private VampireHuntHudPresenter hud;

        private readonly List<PactStackNetworkState> m_Pacts = new List<PactStackNetworkState>();
        private readonly Button[] m_SelectButtons = new Button[3];
        private readonly Action[] m_SelectCallbacks = new Action[3];
        private readonly Label[] m_Names = new Label[3];
        private readonly Label[] m_Descriptions = new Label[3];
        private readonly Label[] m_Stacks = new Label[3];
        private VisualElement m_Overlay;
        private Button m_RerollButton;
        private Label m_RerollHint;
        private bool m_Bound;
        private bool m_CursorCaptured;
        private CursorLockMode m_PreviousLockMode;
        private bool m_PreviousCursorVisible;

        private void Awake()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (draftBridge == null) draftBridge = GetComponent<PactDraftNetworkBridge>();
            if (pactState == null) pactState = GetComponent<PactNetworkState>();
            if (hud == null) hud = GetComponent<VampireHuntHudPresenter>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsOwner) return;
            if (draftBridge != null) draftBridge.DraftChanged += RenderDraft;
            if (pactState != null) pactState.InventoryChanged += RenderSummary;
            StartCoroutine(BindNextFrame());
        }

        public override void OnNetworkDespawn()
        {
            if (draftBridge != null) draftBridge.DraftChanged -= RenderDraft;
            if (pactState != null) pactState.InventoryChanged -= RenderSummary;
            UnbindButtons();
            SetCursorForDraft(false);
            base.OnNetworkDespawn();
        }

        private IEnumerator BindNextFrame()
        {
            yield return null;
            BindUi();
            RenderDraft(draftBridge != null ? draftBridge.CurrentDraft : default);
            RenderSummary();
        }

        private void BindUi()
        {
            if (m_Bound || uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;
            m_Overlay = root.Q<VisualElement>("pact-selection-overlay");
            for (int i = 0; i < 3; i++)
            {
                int capturedIndex = i;
                m_SelectButtons[i] = root.Q<Button>($"pact-select-{i}");
                m_Names[i] = root.Q<Label>($"pact-name-{i}");
                m_Descriptions[i] = root.Q<Label>($"pact-description-{i}");
                m_Stacks[i] = root.Q<Label>($"pact-stack-{i}");
                if (m_SelectButtons[i] != null)
                {
                    m_SelectCallbacks[i] = () => SelectOption(capturedIndex);
                    m_SelectButtons[i].clicked += m_SelectCallbacks[i];
                }
            }
            m_RerollButton = root.Q<Button>("pact-reroll");
            m_RerollHint = root.Q<Label>("pact-reroll-hint");
            if (m_RerollButton != null) m_RerollButton.clicked += Reroll;
            m_Bound = true;
        }

        private void UnbindButtons()
        {
            for (int i = 0; i < m_SelectButtons.Length; i++)
            {
                if (m_SelectButtons[i] != null && m_SelectCallbacks[i] != null)
                    m_SelectButtons[i].clicked -= m_SelectCallbacks[i];
                m_SelectCallbacks[i] = null;
            }
            if (m_RerollButton != null) m_RerollButton.clicked -= Reroll;
            m_Bound = false;
        }

        private void RenderDraft(PactDraftNetworkState draft)
        {
            BindUi();
            if (m_Overlay == null) return;
            m_Overlay.style.display = draft.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
            SetCursorForDraft(draft.IsActive);

            for (int i = 0; i < 3; i++)
            {
                bool available = i < draft.OptionCount;
                VisualElement card = m_SelectButtons[i]?.parent;
                if (card != null) card.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                if (!available) continue;

                uint pactId = draft.GetOption(i);
                int currentStacks = pactState != null ? pactState.GetStacks(pactId) : 0;
                if (catalog != null && catalog.TryGetAsset(pactId, out PactDefinitionAsset asset))
                {
                    if (m_Names[i] != null) m_Names[i].text = asset.DisplayName;
                    if (m_Descriptions[i] != null) m_Descriptions[i].text = asset.Description;
                }
                else
                {
                    if (m_Names[i] != null) m_Names[i].text = $"血契 {pactId}";
                    if (m_Descriptions[i] != null) m_Descriptions[i].text = string.Empty;
                }
                if (m_Stacks[i] != null)
                    m_Stacks[i].text = currentStacks > 0 ? $"当前 {currentStacks} 层 → {currentStacks + 1} 层" : "尚未拥有";
            }

            if (m_RerollButton != null)
            {
                bool canReroll = draft.IsActive && draft.RerollCount < draft.MaxRerolls;
                m_RerollButton.SetEnabled(canReroll);
            }
            if (m_RerollHint != null)
                m_RerollHint.text = draft.IsActive
                    ? draft.RerollCount < draft.MaxRerolls
                        ? $"刷新预付 {draft.RerollCost} 猩红，总消耗仍为 100"
                        : "本次选择已刷新"
                    : string.Empty;
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

        private void SelectOption(int index)
        {
            if (draftBridge == null) return;
            uint pactId = draftBridge.CurrentDraft.GetOption(index);
            if (pactId != 0) draftBridge.SelectPact(pactId);
        }

        private void Reroll() => draftBridge?.Reroll();

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
