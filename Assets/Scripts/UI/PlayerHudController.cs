using UnityEngine;
using UnityEngine.UI;
using VampireHunt.Player.Contracts;
using VampireHunt.UI.Contracts;

/// <summary>
/// Unity view for the lower-left player HUD. It renders only a read model;
/// player lookup and mutable gameplay state remain outside the view.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHudController : MonoBehaviour, IPlayerHudView
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
    [SerializeField, Min(1f)] private float bloodPactMaximum = 100f;

    private void OnEnable()
    {
        SetVisible(!hideUntilLocalPlayerIsReady);
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

    public void Render(IPlayerReadModel model)
    {
        if (model == null)
        {
            SetVisible(!hideUntilLocalPlayerIsReady);
            return;
        }

        HandleHealthChanged(model.Health, model.MaxHealth);
        HandleStaminaChanged(model.Stamina, model.MaxStamina);
        HandleBloodPactChanged(model.Scarlet, bloodPactMaximum);
        SetVisible(true);
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
