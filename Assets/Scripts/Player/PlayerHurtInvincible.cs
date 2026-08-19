using UnityEngine;

/// <summary>
/// Legacy hurt-flash presenter. Invincibility is read from the Player
/// snapshot; this component never starts timers or decides whether damage is
/// legal.
/// </summary>
public sealed class PlayerHurtInvincible : MonoBehaviour
{
    private SpriteRenderer playerSprite;
    private Color originalColor;
    private PlayerNetworkState playerState;

    private void Start()
    {
        playerState = PlayerNetworkState.EnsureForMigration(gameObject);
        playerSprite = GetComponent<SpriteRenderer>();
        if (playerSprite != null) originalColor = playerSprite.color;
        if (playerState != null) playerState.HealthChanged += HandleHealthChanged;
    }

    private void OnDestroy()
    {
        if (playerState != null) playerState.HealthChanged -= HandleHealthChanged;
        RestoreColor();
    }

    private void Update()
    {
        if (playerState == null) return;

        if (playerState.Snapshot.IsInvincibleWindow)
            DoFlashEffect();
        else
            RestoreColor();
    }

    /// <summary>
    /// Compatibility entry retained for AnimationEvents. The authoritative
    /// damage application already opens the invincibility window in Domain.
    /// </summary>
    [System.Obsolete("Invincibility is opened by PlayerVitals through the damage application.")]
    public void EnterInvincibleState()
    {
        // Deliberately no-op: legacy callers must not create a second timer.
    }

    /// <summary>Read-only compatibility check for old callers.</summary>
    public bool CanTakeDamage() =>
        playerState != null &&
        NetworkAuthority.IsServerOrOffline(playerState) &&
        !playerState.Snapshot.IsInvincibleWindow;

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        // The next Update reads the authoritative snapshot. Keeping this
        // callback makes the presenter react to local and replicated hits
        // without owning any gameplay state.
    }

    private void DoFlashEffect()
    {
        if (playerSprite == null) return;
        float frequency = playerState != null ? playerState.FlashSpeed : 10f;
        float alpha = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * frequency) > 0f ? 1f : 0f;
        playerSprite.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
    }

    private void RestoreColor()
    {
        if (playerSprite != null) playerSprite.color = originalColor;
    }
}
