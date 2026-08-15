using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
/// Presents three random player blood pacts when the local player's Scarlet
/// reaches the fixed blood-pact cost. Selection is validated by the server via
/// PlayerNetworkState.RequestBloodPactSelection.
/// </summary>
[DisallowMultipleComponent]
public sealed class BloodPactSelectionController : MonoBehaviour
{
    private const int ChoiceCount = 3;

    [SerializeField] private CanvasGroup overlay;
    [SerializeField] private BloodPactCardView[] cards = new BloodPactCardView[ChoiceCount];
    [SerializeField] private Sprite[] cardEmblems = new Sprite[ChoiceCount];
    [SerializeField] private Text[] localizedTexts;
    [SerializeField, Min(0.05f)] private float playerLookupInterval = 0.25f;
    [SerializeField, Min(0.5f)] private float networkResponseTimeout = 2f;

    private readonly BloodPactDefinition[] currentChoices = new BloodPactDefinition[ChoiceCount];
    private readonly List<BloodPactDefinition> candidateBuffer = new();

    private BloodPactConfig database;
    private PlayerNetworkState boundPlayer;
    private float nextPlayerLookupTime;
    private float pendingTimeoutAt;
    private float previousTimeScale = 1f;
    private bool isShowing;
    private bool isPendingSelection;
    private bool pausedOfflineGame;
    private bool listenersInstalled;
    private string pendingPactId;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

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

    private void Awake()
    {
        InstallButtonListeners();
        ApplyRuntimeChineseFont();
    }

    private void OnEnable()
    {
        database = BloodPactConfig.LoadDefault();
        HideImmediate();
        InstallButtonListeners();
        TryBindLocalPlayer();
    }

    private void OnDisable()
    {
        UnbindPlayer();
        RestorePresentationState();
    }

    private void Update()
    {
        if (isPendingSelection && Time.unscaledTime >= pendingTimeoutAt)
        {
            isPendingSelection = false;
            pendingPactId = null;
            SetCardsInteractable(true);
        }

        if (boundPlayer != null || Time.unscaledTime < nextPlayerLookupTime)
        {
            return;
        }

        nextPlayerLookupTime = Time.unscaledTime + playerLookupInterval;
        TryBindLocalPlayer();
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

    private void TryBindLocalPlayer()
    {
        PlayerNetworkState player = NetworkPlayerRegistry.GetLocalPlayer();
        if (player == null || player == boundPlayer)
        {
            return;
        }

        UnbindPlayer();
        boundPlayer = player;
        boundPlayer.ScarletChanged += HandleScarletChanged;
        boundPlayer.BloodPactsChanged += HandleBloodPactsChanged;
        HandleScarletChanged(boundPlayer.CurrentScarlet, boundPlayer.MaxScarlet);
    }

    private void UnbindPlayer()
    {
        if (boundPlayer != null)
        {
            boundPlayer.ScarletChanged -= HandleScarletChanged;
            boundPlayer.BloodPactsChanged -= HandleBloodPactsChanged;
        }
        boundPlayer = null;
    }

    private void HandleScarletChanged(float current, float maximum)
    {
        if (isPendingSelection && current + 0.0001f < PlayerNetworkState.BloodPactScarletCost)
        {
            HideSelection();
            return;
        }

        if (isShowing && current + 0.0001f < PlayerNetworkState.BloodPactScarletCost)
        {
            HideSelection();
            return;
        }

        if (!isShowing && current + 0.0001f >= PlayerNetworkState.BloodPactScarletCost)
        {
            ShowSelection();
        }
    }

    private void HandleBloodPactsChanged()
    {
        if (!isPendingSelection) return;

        HideSelection();
        if (boundPlayer != null &&
            boundPlayer.CurrentScarlet + 0.0001f >= PlayerNetworkState.BloodPactScarletCost)
        {
            ShowSelection();
        }
    }

    private void ShowSelection()
    {
        if (database == null)
        {
            database = BloodPactConfig.LoadDefault();
        }

        if (database == null || !RollChoices())
        {
            Debug.LogWarning("[Blood Pact UI] Fewer than three implemented player pacts remain.", this);
            return;
        }

        for (int index = 0; index < ChoiceCount; index++)
        {
            Sprite emblem = cardEmblems != null && cardEmblems.Length > 0
                ? cardEmblems[index % cardEmblems.Length]
                : null;
            cards[index]?.SetContent(currentChoices[index], emblem);
        }

        isShowing = true;
        isPendingSelection = false;
        pendingPactId = null;
        SetCardsInteractable(true);
        SetOverlayVisible(true);
        CapturePresentationState();

        if (cards != null && cards.Length > 0 && cards[0]?.Button != null)
        {
            EventSystem.current?.SetSelectedGameObject(cards[0].Button.gameObject);
        }
    }

    private bool RollChoices()
    {
        candidateBuffer.Clear();
        foreach (BloodPactDefinition pact in database.Pacts)
        {
            if (pact != null &&
                pact.IsPlayerPact &&
                pact.IsRuntimeImplemented &&
                (pact.IsRepeatable || boundPlayer == null ||
                 !boundPlayer.HasBloodPact(pact.PactId)))
            {
                candidateBuffer.Add(pact);
            }
        }

        if (candidateBuffer.Count < ChoiceCount)
        {
            return false;
        }

        for (int choice = 0; choice < ChoiceCount; choice++)
        {
            int index = UnityEngine.Random.Range(0, candidateBuffer.Count);
            currentChoices[choice] = candidateBuffer[index];
            candidateBuffer.RemoveAt(index);
        }

        return true;
    }

    private void Choose(int index)
    {
        if (!isShowing || isPendingSelection || boundPlayer == null ||
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
        pendingPactId = pact.PactId;
        pendingTimeoutAt = Time.unscaledTime + networkResponseTimeout;
        SetCardsInteractable(false);

        if (!boundPlayer.RequestBloodPactSelection(pact.PactId))
        {
            isPendingSelection = false;
            pendingPactId = null;
            SetCardsInteractable(true);
        }
    }

    private void HideSelection()
    {
        isShowing = false;
        isPendingSelection = false;
        pendingPactId = null;
        SetOverlayVisible(false);
        RestorePresentationState();
    }

    private void HideImmediate()
    {
        isShowing = false;
        isPendingSelection = false;
        pendingPactId = null;
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

        if (!NetworkAuthority.IsNetworkActive)
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
