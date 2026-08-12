using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyHurtFlash : MonoBehaviour
{
    public Color hurtRed = Color.red;

    private SpriteRenderer enemySprite;
    private Color originalColor;
    private float flashTimer;

    void Start()
    {
        enemySprite = GetComponent<SpriteRenderer>();
        originalColor = enemySprite.color;
    }

    void Update()
    {
        if (flashTimer > 0)
        {
            flashTimer -= Time.deltaTime;
            EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
            float frequency = stats != null ? stats.hurtFlashSpeed : 15f;
            bool showHurtColor =
                Mathf.Sin(Time.time * Mathf.PI * 2f * Mathf.Max(0.01f, frequency)) > 0f;
            if (enemySprite != null)
                enemySprite.color = showHurtColor ? hurtRed : originalColor;
        }
        else
        {
            // 时间结束恢复原本颜色
            if (enemySprite != null) enemySprite.color = originalColor;
        }
    }

    public void StartHurtFlash()
    {
        // 再次受伤直接重置计时。
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        flashTimer = stats != null ? stats.hurtFlashDuration : 0.3f;
    }
}
