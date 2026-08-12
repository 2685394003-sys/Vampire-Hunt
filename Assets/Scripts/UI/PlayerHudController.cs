using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the lower-left player HUD from the local PlayerNetworkState.
/// This controller is intentionally independent from the legacy HPUI,
/// StaminaUI, SPUI and UI components.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHudController : MonoBehaviour
{
    [Header("Visibility")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool hideUntilLocalPlayerIsReady = true;

    [Header("Health")]
    [SerializeField] private RectTransform healthFill;
    [SerializeField] private Text healthValue;

    [Header("Stamina")]
    [SerializeField] private RectTransform staminaFill;
    [SerializeField] private Text staminaValue;

    [Header("Blood Pact / Scarlet")]
    [SerializeField] private RectTransform bloodPactFill;
    [SerializeField] private Text bloodPactValue;

    [SerializeField, Min(0.05f)] private float playerLookupInterval = 0.25f;

    private PlayerNetworkState boundPlayer;
    private float nextPlayerLookupTime;

    private void OnEnable()
    {
        SetVisible(!hideUntilLocalPlayerIsReady);
        TryBindLocalPlayer();
    }

    private void OnDisable()
    {
        UnbindPlayer();
    }

    private void Update()
    {
        if (boundPlayer != null)
        {
            return;
        }

        if (Time.unscaledTime < nextPlayerLookupTime)
        {
            return;
        }

        nextPlayerLookupTime = Time.unscaledTime + playerLookupInterval;
        TryBindLocalPlayer();
    }

    public void Configure(
        CanvasGroup group,
        RectTransform healthBarFill,
        Text healthText,
        RectTransform staminaBarFill,
        Text staminaText,
        RectTransform pactBarFill,
        Text pactText)
    {
        canvasGroup = group;
        healthFill = healthBarFill;
        healthValue = healthText;
        staminaFill = staminaBarFill;
        staminaValue = staminaText;
        bloodPactFill = pactBarFill;
        bloodPactValue = pactText;

        RefreshPreviewValues();
    }

    private void TryBindLocalPlayer()
    {
        PlayerNetworkState localPlayer = NetworkPlayerRegistry.GetLocalPlayer();
        if (localPlayer == null)
        {
            return;
        }

        BindPlayer(localPlayer);
    }

    private void BindPlayer(PlayerNetworkState player)
    {
        if (boundPlayer == player)
        {
            return;
        }

        UnbindPlayer();
        boundPlayer = player;
        boundPlayer.HealthChanged += HandleHealthChanged;
        boundPlayer.StaminaChanged += HandleStaminaChanged;
        boundPlayer.ScarletChanged += HandleBloodPactChanged;

        HandleHealthChanged(boundPlayer.CurrentHealth, boundPlayer.MaxHealth);
        HandleStaminaChanged(boundPlayer.CurrentStamina, boundPlayer.MaxStamina);
        HandleBloodPactChanged(boundPlayer.CurrentScarlet, boundPlayer.MaxScarlet);
        SetVisible(true);
    }

    private void UnbindPlayer()
    {
        if (boundPlayer == null)
        {
            return;
        }

        boundPlayer.HealthChanged -= HandleHealthChanged;
        boundPlayer.StaminaChanged -= HandleStaminaChanged;
        boundPlayer.ScarletChanged -= HandleBloodPactChanged;
        boundPlayer = null;
    }

    private void HandleHealthChanged(int current, int maximum)
    {
        SetFill(healthFill, current, maximum);
        SetValueText(healthValue, current, maximum);
    }

    private void HandleStaminaChanged(float current, float maximum)
    {
        SetFill(staminaFill, current, maximum);
        SetValueText(staminaValue, current, maximum);
    }

    private void HandleBloodPactChanged(float current, float maximum)
    {
        SetFill(bloodPactFill, current, maximum);
        SetValueText(bloodPactValue, current, maximum);
    }

    private void RefreshPreviewValues()
    {
        HandleHealthChanged(86, 100);
        HandleStaminaChanged(64f, 100f);
        HandleBloodPactChanged(42f, 100f);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private static void SetFill(RectTransform fill, float current, float maximum)
    {
        if (fill == null)
        {
            return;
        }

        float ratio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
        Vector2 anchorMax = fill.anchorMax;
        anchorMax.x = ratio;
        fill.anchorMax = anchorMax;
    }

    private static void SetValueText(Text label, float current, float maximum)
    {
        if (label == null)
        {
            return;
        }

        label.text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(maximum)}";
    }
}
