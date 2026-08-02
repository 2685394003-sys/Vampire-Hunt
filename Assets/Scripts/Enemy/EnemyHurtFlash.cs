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
            // 持续保持红色，不再来回切换
            enemySprite.color = hurtRed;
        }
        else
        {
            // 时间结束恢复原本颜色
            enemySprite.color = originalColor;
        }
    }

    public void StartHurtFlash()
    {
        // 再次受伤直接重置计时，重新保持红色
        flashTimer = StatsManager.Instance.enemyflashDuration;
    }
}