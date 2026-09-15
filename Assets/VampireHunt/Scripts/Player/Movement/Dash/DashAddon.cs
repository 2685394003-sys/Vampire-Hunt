using UnityEngine;
using Unity.Netcode;
using UnityEngine.Serialization;
using Blocks.Gameplay.Core;
using VampireHunt.Contracts;

public class DashAddon : NetworkBehaviour, IPlayerAddon
{
    [Header("References")]
    [SerializeField] private DashAbility dashAbility;
    [FormerlySerializedAs("onDashInputChanged")]
    [Tooltip("Button-press event used to trigger Dash.")]
    [SerializeField] private GameEvent onDashPressed;

    [Header("Feedback")]
    [SerializeField] private SoundDef insufficientStaminaSound;
    [SerializeField] private float warningCooldown = 2.0f;

    private CorePlayerManager m_PlayerManager;
    private CoreStatsHandler m_StatsHandler;
    private IAttributeModifierTarget m_AttributeModifiers;
    private float m_LastWarningTime;

    public void Initialize(CorePlayerManager playerManager)
    {
        m_PlayerManager = playerManager;
        m_StatsHandler = playerManager.CoreStats;
        var behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length && m_AttributeModifiers == null; i++)
            if (behaviours[i] is IAttributeModifierTarget target) m_AttributeModifiers = target;
        if (dashAbility == null)
        {
            dashAbility = GetComponent<DashAbility>();
        }
    }

    // Enable input listening when player spawns
    public void OnPlayerSpawn()
    {
        if (!m_PlayerManager.IsOwner) return;
        if (onDashPressed != null)
        {
            onDashPressed.RegisterListener(HandleDashPressed);
        }
    }

    // Cleanup when player despawns
    public void OnPlayerDespawn()
    {
        if (!m_PlayerManager.IsOwner) return;
        if (onDashPressed != null)
        {
            onDashPressed.UnregisterListener(HandleDashPressed);
        }
    }

    public void OnLifeStateChanged(PlayerLifeState previousState, PlayerLifeState newState) { }

    private void HandleDashPressed()
    {
        if (dashAbility == null)
        {
            Debug.LogWarning("[DashAddon] DashAbility component not found.", this);
            return;
        }

        float staminaCost = m_AttributeModifiers != null
            ? m_AttributeModifiers.GetFinalAttributeValue(StatKeys.DashStaminaCost)
            : dashAbility.StaminaCost;
        // Get current stamina from stats system
        float currentStamina = m_StatsHandler.GetCurrentValue(StatKeys.Stamina);

        // Check Stamina
        if (currentStamina < staminaCost)
        {
            HandleInsufficientStamina();
            return;
        }

        // Attempt Dash
        if (dashAbility.TryActivate())
        {
            // Consume Stamina
            m_StatsHandler.ModifyStat(
                StatKeys.Stamina,
                -staminaCost,
                m_PlayerManager.OwnerClientId,
                ModificationSource.Consumption
            );
        }
    }

    private void HandleInsufficientStamina()
    {
        // Prevent spamming the warning sound
        if (Time.time - m_LastWarningTime < warningCooldown)
        {
            return;
        }

        m_LastWarningTime = Time.time;
        if (insufficientStaminaSound != null)
        {
            CoreDirector.RequestAudio(insufficientStaminaSound)
                .AttachedTo(transform)
                .Play(0.5f);
        }
    }
}

