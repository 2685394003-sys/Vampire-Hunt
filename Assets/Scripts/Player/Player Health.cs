using UnityEngine;

/// <summary>
/// Legacy health facade kept for Prefab/UnityEvent compatibility. All health
/// transitions are delegated to PlayerNetworkState's Player runtime port.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerNetworkState))]
public sealed class PlayerHealth : MonoBehaviour
{
    public PlayerHurtInvincible invincible;

    private PlayerNetworkState state;

    private void Awake()
    {
        state = PlayerNetworkState.EnsureForMigration(gameObject);
    }

    public void ChangeHealth(int amount)
    {
        if (amount <= 0 || state == null) return;
        // The runtime port performs authority, invincibility and duplicate-hit
        // handling. The presenter must not pre-filter this command.
        state.ApplyDamage(amount);
    }

    public void ReHealth() => state?.HealToFull();

    public void ForceDeath() => state?.ForceDeath();
}
