using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHurtInvincible : MonoBehaviour
{

    private float invincibleTimer;
    private SpriteRenderer playerSprite;
    private Color originalColor;

    void Start()
    {
        playerSprite = GetComponent<SpriteRenderer>();
        originalColor = playerSprite.color;
    }

    void Update()
    {
        // 无敌倒计时
        if (invincibleTimer > 0)
        {
            invincibleTimer -= Time.deltaTime;
            DoFlashEffect();
        }
        else
        {
            // 无敌结束，恢复正常不透明
            playerSprite.color = originalColor;
        }
    }

    // 受伤时外部调用，开启无敌+闪烁
    public void EnterInvincibleState()
    {
    invincibleTimer = StatsManager.Instance.invincibleTime;
    }

    // 透明度交替闪烁
    void DoFlashEffect()
    {
        // 0完全透明，1不透明，交替切换
        float alpha = Mathf.Sin(Time.time * Mathf.PI * 2 / StatsManager.Instance.flashSpeed) > 0 ? 1f : 0f;
        playerSprite.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
    }

    // 扣血专用校验函数：返回true代表可以扣血，false无敌不扣
    public bool CanTakeDamage()
    {
        return invincibleTimer <= 0;
    }
    
}
