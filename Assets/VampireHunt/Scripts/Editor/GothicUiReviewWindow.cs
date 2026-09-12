using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Editor
{
    /// <summary>Isolated art review. Uses shipping UXML/USS and real catalog copy, never sends gameplay commands.</summary>
    public sealed class GothicUiReviewWindow : EditorWindow
    {
        private const string HudPath = "Assets/VampireHunt/Scripts/Presentation/HUD/VampireHuntHUD.uxml";
        private const string LibraryPath = "Assets/VampireHunt/Scripts/Presentation/HUD/GothicComponents.uxml";
        private VisualElement m_Viewport;
        private VisualElement m_Canvas;
        private VisualElement m_Ui;
        private int m_Page;
        private int m_Pact = -1;
        private int m_Affix = -1;
        private int m_Roll;
        private int m_Width = 1920;
        private int m_Height = 1080;
        private readonly string[] m_PactNames = new string[3];
        private readonly string[] m_AffixNames = new string[3];

        [MenuItem("Vampire Hunt/UI/Gothic UI Review")]
        public static void Open()
        {
            var window = GetWindow<GothicUiReviewWindow>();
            window.titleContent = new GUIContent("血族狂猎 · UI Review");
            window.minSize = new Vector2(800, 510);
            window.Show();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 34 } };
            toolbar.Add(new Button(() => ShowPage(0)) { text = "HUD" });
            toolbar.Add(new Button(() => ShowPage(1)) { text = "血契选择" });
            toolbar.Add(new Button(() => ShowPage(2)) { text = "组件库" });
            var resolutions = new PopupField<string>(new System.Collections.Generic.List<string>
                { "1920 × 1080", "1280 × 720", "2560 × 1440", "2560 × 1080" }, 0);
            resolutions.RegisterValueChangedCallback(e =>
            {
                var parts = e.newValue.Split('×');
                m_Width = int.Parse(parts[0].Trim()); m_Height = int.Parse(parts[1].Trim());
                Fit();
            });
            toolbar.Add(resolutions);
            toolbar.Add(new Button(() => ShowPage(m_Page)) { text = "重新载入样式" });
            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(new Label("视觉预览 · 示例数值 · 血契文案来自项目配置 · 不修改游戏状态")
                { style = { height = 26, unityTextAlign = TextAnchor.MiddleCenter } });
            m_Viewport = new VisualElement { style = { flexGrow = 1, overflow = Overflow.Hidden, backgroundColor = new Color(0.045f, 0.06f, 0.055f) } };
            rootVisualElement.Add(m_Viewport);
            m_Viewport.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            ShowPage(0);
        }

        private void Fit()
        {
            if (m_Canvas == null) return;
            float logicalWidth = Mathf.Max(1920f, m_Width * (1080f / m_Height));
            float logicalHeight = Mathf.Max(1080f, m_Height * (1920f / m_Width));
            float scale = Mathf.Min(m_Viewport.contentRect.width / logicalWidth, m_Viewport.contentRect.height / logicalHeight);
            m_Canvas.style.width = logicalWidth;
            m_Canvas.style.height = logicalHeight;
            m_Canvas.style.transformOrigin = new TransformOrigin(0, 0, 0);
            m_Canvas.style.scale = new Scale(new Vector3(scale, scale, 1));
            m_Canvas.style.left = Mathf.Max(0, (m_Viewport.contentRect.width - logicalWidth * scale) * 0.5f);
            m_Canvas.style.top = Mathf.Max(0, (m_Viewport.contentRect.height - logicalHeight * scale) * 0.5f);
        }

        private void ShowPage(int page)
        {
            m_Page = page; m_Pact = m_Affix = -1; m_Roll = 0;
            m_Viewport.Clear();
            m_Canvas = new VisualElement { style = { position = Position.Absolute } };
            m_Viewport.Add(m_Canvas);
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(page == 2 ? LibraryPath : HudPath);
            if (tree == null) { m_Canvas.Add(new Label("UI asset not found")); return; }
            m_Ui = tree.CloneTree();
            m_Ui.style.flexGrow = 1;
            m_Canvas.Add(m_Ui);
            if (page != 2)
            {
                SetLabel("health-value", "720 / 1000"); SetBar("player-health-bar", 72);
                SetLabel("stamina-value", "80 / 100"); SetBar("player-stamina-bar", 80);
                SetLabel("scarlet-value", "350 / 2000");
                m_Ui.Q("scarlet-fill").style.height = Length.Percent(100);
                m_Ui.Q("hud-root").AddToClassList("hud--upgrade-ready");
                SetLabel("level-up-hint", "Z  缔结血契 · 需要 121 猩红");
                SetLabel("run-timer", "08:42"); SetLabel("pact-count", "6");
                SetLabel("build-name", "血刃狂增 · 震骨");
                m_Ui.Q("boss-panel").style.display = DisplayStyle.Flex;
                SetLabel("boss-name", "猩红之主"); SetLabel("boss-health-value", "3600 / 5000");
                SetLabel("boss-guard-value", "300 / 500"); SetBar("boss-health-bar", 72); SetBar("boss-guard-bar", 60);
                if (page == 1)
                {
                    m_Ui.Q("pact-selection-overlay").style.display = DisplayStyle.Flex;
                    for (int i = 0; i < 3; i++)
                    {
                        int index = i;
                        m_Ui.Q<Button>($"pact-select-{i}").clicked += () => { m_Pact = index; RenderSelection(); };
                        m_Ui.Q<Button>($"affix-select-{i}").clicked += () => { m_Affix = index; RenderSelection(); };
                    }
                    m_Ui.Q<Button>("pact-reroll").clicked += () => { m_Roll++; m_Pact = m_Affix = -1; FillDraft(); };
                    m_Ui.Q<Button>("pact-confirm").clicked += () =>
                    {
                        SetLabel("pact-selection-summary", $"预览已确认：{m_PactNames[m_Pact]} / {m_AffixNames[m_Affix]}（未扣除游戏资源）");
                        m_Ui.Q<Button>("pact-confirm").SetEnabled(false);
                    };
                    FillDraft();
                }
            }
            Fit();
        }

        private void FillDraft()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PactCatalogAsset>("Assets/VampireHunt/Data/Pacts/PactCatalog.asset");
            var affixes = AssetDatabase.LoadAssetAtPath<EnemyAffixCatalogAsset>("Assets/VampireHunt/Data/EnemyAffixes/EnemyAffixCatalog.asset");
            uint[] ids = m_Roll == 0 ? new uint[] { 1001, 1004, 9008 } : new uint[] { 3001, 7001, 8001 };
            var affixCatalog = affixes.CreateCatalog();
            for (int i = 0; i < 3; i++)
            {
                if (catalog.TryGetAsset(ids[i], out var pact))
                {
                    m_PactNames[i] = pact.DisplayName; SetLabel($"pact-name-{i}", pact.DisplayName);
                    SetLabel($"pact-description-{i}", pact.Description);
                    var icon = m_Ui.Q($"pact-icon-{i}");
                    icon.EnableInClassList("pact-art--blade", ids[i] >= 1001 && ids[i] <= 1005);
                    icon.style.backgroundImage = pact.Icon != null ? new StyleBackground(pact.Icon) : StyleKeyword.Null;
                }
                if (i < affixCatalog.All.Count && affixes.TryGetAsset(affixCatalog.All[i].AffixId, out var affix))
                {
                    m_AffixNames[i] = affix.DisplayName; SetLabel($"affix-name-{i}", affix.DisplayName);
                    SetLabel($"affix-description-{i}", affix.Description);
                }
            }
            SetLabel("pact-draft-subtitle", "选择一份血契和一份副契，确认后消耗 121 猩红");
            SetLabel("pact-reroll-hint", m_Roll == 0 ? "刷新两排选项，预付 25 猩红，总消耗仍为 121" : "本次选择已刷新");
            m_Ui.Q<Button>("pact-reroll").SetEnabled(m_Roll == 0);
            RenderSelection();
        }

        private void RenderSelection()
        {
            for (int i = 0; i < 3; i++)
            {
                m_Ui.Q($"pact-card-{i}").EnableInClassList("draft-card--selected", i == m_Pact);
                m_Ui.Q($"affix-card-{i}").EnableInClassList("draft-card--selected", i == m_Affix);
                m_Ui.Q<Button>($"pact-select-{i}").text = i == m_Pact ? "已选择" : "选择";
                m_Ui.Q<Button>($"affix-select-{i}").text = i == m_Affix ? "已选择" : "选择";
            }
            SetLabel("pact-selection-summary", $"血契：{(m_Pact < 0 ? "未选择" : m_PactNames[m_Pact])}    /    副契：{(m_Affix < 0 ? "未选择" : m_AffixNames[m_Affix])}");
            m_Ui.Q<Button>("pact-confirm").SetEnabled(m_Pact >= 0 && m_Affix >= 0);
        }

        private void SetLabel(string name, string text) { var label = m_Ui.Q<Label>(name); if (label != null) label.text = text; }
        private void SetBar(string name, float percent) { var bar = m_Ui.Q<ProgressBar>(name); if (bar != null) { bar.lowValue = 0; bar.highValue = 100; bar.value = percent; } }
    }
}
