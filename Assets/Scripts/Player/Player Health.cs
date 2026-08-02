using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    // 变量必须放在class大括号内部
    public PlayerHurtInvincible invincible;

    public void ChangeHealth(int amount)
    {
        if (amount <= 0 || StatsManager.Instance == null)
        {
            return;
        }

        if (invincible == null || invincible.CanTakeDamage())
        {
            StatsManager.Instance.currentHealth -= amount;
            if (invincible != null)
            {
                invincible.EnterInvincibleState();
            }

            if (StatsManager.Instance.currentHealth <= 0)
            {
                ForceDeath();
                return;
            }
            
            if (StatsManager.Instance.currentHealth > StatsManager.Instance.maxHealth)
            {
            ReHealth();
            }
        }
    }

    public void ReHealth()
    {
        if (StatsManager.Instance == null)
        {
            return;
        }

        StatsManager.Instance.currentHealth = StatsManager.Instance.maxHealth;
        gameObject.SetActive(true);
    }

    public void ForceDeath()
    {
        if (StatsManager.Instance != null)
        {
            StatsManager.Instance.currentHealth = 0;
        }

        gameObject.SetActive(false);
    }
}
