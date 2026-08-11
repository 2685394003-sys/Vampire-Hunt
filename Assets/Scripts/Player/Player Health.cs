using UnityEngine;

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
        if (!NetworkAuthority.IsServerOrOffline(state) || amount <= 0 || state == null)
        {
            return;
        }

        if (invincible != null && !invincible.CanTakeDamage())
        {
            return;
        }

        if (state.ApplyDamage(amount))
        {
            invincible?.EnterInvincibleState();
        }
    }

    public void ReHealth()
    {
        state?.HealToFull();
    }

    public void ForceDeath()
    {
        state?.ForceDeath();
    }
}
