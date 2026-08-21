using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VampireHunt.Player.Contracts;
using VampireHunt.UI;
using VampireHunt.UI.Contracts;

[Serializable]
public sealed class BloodPactCardView
{
    [SerializeField] private GameObject root;
    [SerializeField] private Button button;
    [SerializeField] private Image emblem;
    [SerializeField] private Text title;
    [SerializeField] private Text tier;
    [SerializeField] private Text description;

    public Button Button => button;

    public BloodPactCardView(
        GameObject cardRoot,
        Button cardButton,
        Image cardEmblem,
        Text cardTitle,
        Text cardTier,
        Text cardDescription)
    {
        root = cardRoot;
        button = cardButton;
        emblem = cardEmblem;
        title = cardTitle;
        tier = cardTier;
        description = cardDescription;
    }

    public void SetContent(BloodPactDefinition pact, Sprite icon)
    {
        if (root != null) root.SetActive(pact != null);
        if (pact == null) return;

        if (emblem != null) emblem.sprite = icon;
        if (title != null) title.text = pact.DisplayName;
        if (tier != null)
        {
            tier.text = pact.IsRepeatable
                ? $"TIER {pact.Tier} · 可重复"
                : $"TIER {pact.Tier}";
        }
        if (description != null) description.text = pact.BuildCardDescription();
    }

    public void SetInteractable(bool value)
    {
        if (button != null) button.interactable = value;
    }
}

/// <summary>
/// Presents the authoritative BloodPactOffer supplied by the Player runtime.
/// Choice generation, availability, cost and selection are all owned by the
/// Player Application; this component only maps ids to authored card views.
/// </summary>
[DisallowMultipleComponent]
public sealed class BloodPactSelectionController : MonoBehaviour,
    IBloodPactSelectionView, IBloodPactSelectionPresenterBinding,
    INetworkActivityProviderBinding
{
    private const int ChoiceCount = 3;

    [SerializeField] private CanvasGroup overlay;
    [SerializeField] private BloodPactCardView[] cards = new BloodPactCardView[ChoiceCount];
    [SerializeField] private Sprite[] cardEmblems = new Sprite[ChoiceCount];
    [SerializeField] private Text[] localizedTexts;
    [SerializeField] private BloodPactConfig database;

    private readonly BloodPactDefinition[] currentChoices = new BloodPactDefinition[ChoiceCount];
    private BloodPactSelectionPresenter presenter;
    private BloodPactOffer currentOffer;
    private float previousTimeScale = 1f;
    private bool isShowing;
    private bool isPendingSelection;
    private bool pausedOfflineGame;
    private bool listenersInstalled;
    private uint suppressedOfferVersion;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private Func<bool> networkActiveProvider;

    public void Configure(
        CanvasGroup canvasGroup,
        BloodPactCardView[] cardViews,
        Sprite[] emblems,
        Text[] texts)
    {
        overlay = canvasGroup;
        cards = cardViews;
        cardEmblems = emblems;
        localizedTexts = texts;
    }

    public void SetCatalog(BloodPactConfig value) => database = value;

    public void Bind(BloodPactSelectionPresenter value)
    {
        presenter = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Supplies the composition-owned network mode query used only for local
    /// pause behavior. The view never reaches into NetworkManager or domain
    /// state on its own.
    /// </summary>
    public void SetNetworkActiveProvider(Func<bool> provider)
    {
        networkActiveProvider = provider;
    }

    private void Awake()
    {
        InstallButtonListeners();
        ApplyRuntimeChineseFont();
    }

    private void OnEnable()
    {
        HideImmediate();
        InstallButtonListeners();
    }

    private void OnDisable()
    {
        RestorePresentationState();
    }

    private void InstallButtonListeners()
    {
        if (listenersInstalled || cards == null)
        {
            return;
        }

        for (int index = 0; index < cards.Length; index++)
        {
            int choiceIndex = index;
            Button button = cards[index]?.Button;
            if (button != null)
            {
                button.onClick.AddListener(() => Choose(choiceIndex));
            }
        }

        listenersInstalled = true;
    }

    public void ShowOffer(BloodPactOffer offer)
    {
        if (!offer.IsValid || offer.OfferVersion == suppressedOfferVersion)
        {
            Hide();
            return;
        }

        currentOffer = offer;
        if (database == null ||
            !RollChoices())
        {
            Debug.LogWarning(
                "[Blood Pact UI] An explicit BloodPactConfig is missing or the offer cannot be mapped.",
                this);
            Hide();
            return;
        }

        for (int index = 0; index < ChoiceCount; index++)
        {
            Sprite emblem = cardEmblems != null && cardEmblems.Length > 0
                ? cardEmblems[index % cardEmblems.Length]
                : null;
            cards[index]?.SetContent(currentChoices[index], emblem);
        }

        if (!isShowing) CapturePresentationState();
        isShowing = true;
        isPendingSelection = false;
        SetCardsInteractable(true);
        SetOverlayVisible(true);

        if (cards != null && cards.Length > 0 && cards[0]?.Button != null)
        {
            EventSystem.current?.SetSelectedGameObject(cards[0].Button.gameObject);
        }
    }

    private bool RollChoices()
    {
        for (int choice = 0; choice < ChoiceCount; choice++)
        {
            if (choice >= currentOffer.Choices.Count ||
                !database.TryGet(currentOffer.Choices[choice].Value, out BloodPactDefinition pact))
                return false;
            currentChoices[choice] = pact;
        }

        return true;
    }

    private void Choose(int index)
    {
        if (!isShowing || isPendingSelection || presenter == null ||
            index < 0 || index >= currentChoices.Length)
        {
            return;
        }

        BloodPactDefinition pact = currentChoices[index];
        if (pact == null)
        {
            return;
        }

        isPendingSelection = true;
        SetCardsInteractable(false);

        CommandResult result = presenter.Select(new BloodPactId(pact.PactId));
        if (result.Accepted)
        {
            suppressedOfferVersion = currentOffer.OfferVersion;
        }
        else
        {
            isPendingSelection = false;
            SetCardsInteractable(true);
        }
    }

    public void Hide()
    {
        isShowing = false;
        isPendingSelection = false;
        SetOverlayVisible(false);
        RestorePresentationState();
    }

    private void HideImmediate()
    {
        isShowing = false;
        isPendingSelection = false;
        SetOverlayVisible(false);
    }

    private void SetOverlayVisible(bool value)
    {
        if (overlay == null) return;
        overlay.alpha = value ? 1f : 0f;
        overlay.interactable = value;
        overlay.blocksRaycasts = value;
    }

    private void SetCardsInteractable(bool value)
    {
        if (cards == null) return;
        foreach (BloodPactCardView card in cards)
        {
            card?.SetInteractable(value);
        }
    }

    private void CapturePresentationState()
    {
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        bool networkActive = networkActiveProvider != null && networkActiveProvider();
        if (!networkActive)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            pausedOfflineGame = true;
        }
    }

    private void RestorePresentationState()
    {
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;

        if (pausedOfflineGame)
        {
            Time.timeScale = previousTimeScale;
            pausedOfflineGame = false;
        }
    }

    private void ApplyRuntimeChineseFont()
    {
        Font chineseFont = Font.CreateDynamicFontFromOSFont(
            new[]
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "PingFang SC",
                "Noto Sans CJK SC",
                "SimHei"
            },
            28);

        if (chineseFont == null || localizedTexts == null)
        {
            return;
        }

        foreach (Text text in localizedTexts)
        {
            if (text != null) text.font = chineseFont;
        }
    }
}
