using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Host-only in-game UI for reversible Boss debug commands.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class BossDebugPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private BossDebugController controller;
        [SerializeField] private BossEncounterStateReplicator encounterState;
        [SerializeField] private BossAbilityStateReplicator abilityState;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;
        [Min(.05f)] [SerializeField] private float statusRefreshInterval = .15f;

        private readonly List<VisualElement> m_CommandControls = new List<VisualElement>();
        private VisualElement m_Panel;
        private Toggle m_MasterToggle;
        private Toggle m_MovementToggle;
        private Button m_Stage1;
        private Button m_Stage2;
        private Button m_Stage3;
        private Button m_CancelAbility;
        private Label m_Status;
        private Label m_Feedback;
        private VisualElement m_AbilityList;
        private bool m_Bound;
        private float m_NextStatusRefresh;

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (controller == null) controller = GetComponent<BossDebugController>();
            if (encounterState == null) encounterState = GetComponent<BossEncounterStateReplicator>();
            if (abilityState == null) abilityState = GetComponent<BossAbilityStateReplicator>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
        }

        private void OnEnable()
        {
            TryBind();
        }

        private void Update()
        {
            if (!m_Bound && !TryBind()) return;
            if (Time.unscaledTime < m_NextStatusRefresh) return;
            m_NextStatusRefresh = Time.unscaledTime + statusRefreshInterval;
            RefreshState();
        }

        private void OnDisable()
        {
            if (controller != null) controller.SetDebugEnabled(false);
            if (m_Panel != null) m_Panel.style.display = DisplayStyle.None;
            Unbind();
        }

        private bool TryBind()
        {
            if (m_Bound || document == null || document.rootVisualElement == null) return m_Bound;
            VisualElement root = document.rootVisualElement;
            m_Panel = root.Q<VisualElement>("boss-debug-panel");
            m_MasterToggle = root.Q<Toggle>("boss-debug-enabled");
            m_MovementToggle = root.Q<Toggle>("boss-debug-pause-movement");
            m_Stage1 = root.Q<Button>("boss-debug-stage-1");
            m_Stage2 = root.Q<Button>("boss-debug-stage-2");
            m_Stage3 = root.Q<Button>("boss-debug-stage-3");
            m_CancelAbility = root.Q<Button>("boss-debug-cancel-ability");
            m_Status = root.Q<Label>("boss-debug-status");
            m_Feedback = root.Q<Label>("boss-debug-feedback");
            m_AbilityList = root.Q<VisualElement>("boss-debug-ability-list");
            if (m_MasterToggle == null || m_MovementToggle == null || m_AbilityList == null) return false;

            m_MasterToggle.RegisterValueChangedCallback(HandleMasterChanged);
            m_MovementToggle.RegisterValueChangedCallback(HandleMovementChanged);
            if (m_Stage1 != null) m_Stage1.clicked += ForceStage1;
            if (m_Stage2 != null) m_Stage2.clicked += ForceStage2;
            if (m_Stage3 != null) m_Stage3.clicked += ForceStage3;
            if (m_CancelAbility != null) m_CancelAbility.clicked += CancelAbility;

            m_CommandControls.Clear();
            m_CommandControls.Add(m_MovementToggle);
            AddCommandControl(m_Stage1);
            AddCommandControl(m_Stage2);
            AddCommandControl(m_Stage3);
            AddCommandControl(m_CancelAbility);
            BuildAbilityButtons();
            m_Bound = true;
            RefreshState();
            return true;
        }

        private void BuildAbilityButtons()
        {
            m_AbilityList.Clear();
            BossPhaseSetAsset phaseSet = phaseProvider != null ? phaseProvider.PhaseSet : null;
            if (phaseSet == null)
            {
                m_AbilityList.Add(new Label("没有找到 Boss Phase Set"));
                return;
            }

            var seen = new HashSet<uint>();
            IReadOnlyList<BossPhaseAsset> phases = phaseSet.Phases;
            for (int phaseIndex = 0; phaseIndex < phases.Count; phaseIndex++)
            {
                BossPhaseAsset phase = phases[phaseIndex];
                if (phase == null) continue;
                IReadOnlyList<BossPhaseAbilityEntryAsset> entries = phase.Abilities;
                for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    BossAbilityAsset ability = entries[entryIndex]?.Ability;
                    if (ability == null || !seen.Add(ability.AbilityId)) continue;
                    uint abilityId = ability.AbilityId;
                    var button = new Button(() => ForceAbility(abilityId))
                    {
                        text = $"{abilityId}  {ability.DisplayName}"
                    };
                    button.AddToClassList("boss-debug__ability-button");
                    m_AbilityList.Add(button);
                    m_CommandControls.Add(button);
                }
            }
        }

        private void HandleMasterChanged(ChangeEvent<bool> evt)
        {
            bool success = controller != null && controller.SetDebugEnabled(evt.newValue);
            if (!success) m_MasterToggle.SetValueWithoutNotify(false);
            SetFeedback(success
                ? evt.newValue ? "Debug 控制已启用" : "Debug 已关闭，Boss 恢复正常逻辑"
                : "只有 Host / 服务器可以启用 Boss Debug");
            RefreshState();
        }

        private void HandleMovementChanged(ChangeEvent<bool> evt)
        {
            bool success = controller != null && controller.TrySetMovementPaused(evt.newValue);
            if (!success) m_MovementToggle.SetValueWithoutNotify(controller != null && controller.MovementPaused);
            SetFeedback(success ? evt.newValue ? "Boss 位移已暂停" : "Boss 位移已恢复" : "暂停位移失败");
            RefreshState();
        }

        private void ForceStage1() => ForceStage(1);
        private void ForceStage2() => ForceStage(2);
        private void ForceStage3() => ForceStage(3);

        private void ForceStage(int stageNumber)
        {
            bool success = controller != null && controller.TryForceStage(stageNumber);
            SetFeedback(success ? $"已切换到 Boss 阶段 {stageNumber}" : $"切换阶段 {stageNumber} 失败");
            RefreshState();
        }

        private void ForceAbility(uint abilityId)
        {
            string name = ResolveAbilityName(abilityId);
            bool success = controller != null && controller.TryForceAbility(abilityId);
            SetFeedback(success ? $"强制释放：{name}" : $"释放失败：{name}");
            RefreshState();
        }

        private void CancelAbility()
        {
            bool success = controller != null && controller.TryCancelAbility();
            SetFeedback(success ? "当前技能已取消" : "取消技能失败");
            RefreshState();
        }

        private void RefreshState()
        {
            if (!m_Bound) return;
            bool isServer = controller != null && controller.HasServerAuthority;
            bool debugEnabled = isServer && controller.DebugEnabled;
            if (m_Panel != null) m_Panel.style.display = isServer ? DisplayStyle.Flex : DisplayStyle.None;
            m_MasterToggle.SetEnabled(isServer);
            m_MasterToggle.SetValueWithoutNotify(debugEnabled);
            m_MovementToggle.SetValueWithoutNotify(controller != null && controller.MovementPaused);
            for (int i = 0; i < m_CommandControls.Count; i++)
                m_CommandControls[i]?.SetEnabled(debugEnabled);

            BossEncounterNetworkState encounter = encounterState != null ? encounterState.Current : default;
            BossAbilityNetworkState ability = abilityState != null ? abilityState.CurrentState : default;
            string authority = isServer ? "Host/服务器" : "等待 Host 权限";
            string casting = ability.IsCasting ? ResolveAbilityName(ability.AbilityId) : "无";
            if (m_Status != null)
                m_Status.text = $"{authority}  |  阶段 {Mathf.Max(1, encounter.StageNumber)}  |  " +
                                $"状态 {encounter.State}  |  当前技能 {casting}  |  " +
                                $"施法人数 {Mathf.Max(1, ability.ParticipantCount)}";
        }

        private string ResolveAbilityName(uint abilityId)
        {
            return phaseProvider != null && phaseProvider.TryGetAbility(abilityId, out BossAbilityAsset ability)
                ? ability.DisplayName
                : abilityId == 0 ? "无" : abilityId.ToString();
        }

        private void SetFeedback(string message)
        {
            if (m_Feedback != null) m_Feedback.text = message ?? string.Empty;
        }

        private void AddCommandControl(VisualElement element)
        {
            if (element != null) m_CommandControls.Add(element);
        }

        private void Unbind()
        {
            if (!m_Bound) return;
            m_MasterToggle.UnregisterValueChangedCallback(HandleMasterChanged);
            m_MovementToggle.UnregisterValueChangedCallback(HandleMovementChanged);
            if (m_Stage1 != null) m_Stage1.clicked -= ForceStage1;
            if (m_Stage2 != null) m_Stage2.clicked -= ForceStage2;
            if (m_Stage3 != null) m_Stage3.clicked -= ForceStage3;
            if (m_CancelAbility != null) m_CancelAbility.clicked -= CancelAbility;
            m_CommandControls.Clear();
            m_Bound = false;
        }
    }
}
