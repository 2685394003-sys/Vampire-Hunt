using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHurtInvincible : MonoBehaviour
{

    private float invincibleTimer;
    private SpriteRenderer playerSprite;
    private Color originalColor;
    private PlayerNetworkState playerState;

    void Start()
    {
        playerState = PlayerNetworkState.EnsureForMigration(gameObject);
        playerSprite = GetComponent<SpriteRenderer>();
        if (playerSprite != null) originalColor = playerSprite.color;
    }

    void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline(playerState))
            return;

        // 无敌倒计时
        if (invincibleTimer > 0)
        {
            invincibleTimer -= Time.deltaTime;
            DoFlashEffect();
        }
        else
        {
            // 无敌结束，恢复正常不透明
            if (playerSprite != null) playerSprite.color = originalColor;
        }
    }

    // 受伤时外部调用，开启无敌+闪烁
    public void EnterInvincibleState()
    {
        if (!NetworkAuthority.IsServerOrOffline(playerState))
            return;

        invincibleTimer = playerState != null ? playerState.InvincibleTime : 0f;
    }

    // 透明度交替闪烁
    void DoFlashEffect()
    {
        // 0完全透明，1不透明，交替切换
        if (playerSprite == null) return;
        float frequency = playerState != null ? playerState.FlashSpeed : 10f;
        float alpha = Mathf.Sin(Time.time * Mathf.PI * 2f * frequency) > 0f ? 1f : 0f;
        playerSprite.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
    }

    // 扣血专用校验函数：返回true代表可以扣血，false无敌不扣
    public bool CanTakeDamage()
    {
        if (!NetworkAuthority.IsServerOrOffline(playerState))
            return false;

        return invincibleTimer <= 0;
    }
    
}
