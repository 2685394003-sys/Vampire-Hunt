using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StatsManager : MonoBehaviour
{
    public static StatsManager Instance;

    [Header("玩家数值 - 战斗 / Player - Combat")]
    public int damage;
    public float weaponRange;
    public float knockbackForce;
    public float knockbackTime;
    public float stunTime;
    public float cooldown;

    [Header("玩家数值 - 移动 / Player - Movement")]
    public int speed;

    [Header("玩家数值 - 体力系统 / Player - Stamina")]
    public float maxStamina = 100f;        // 最大体力
    public float staminaRecoverSpeed = 12f;// 每秒恢复体力值
    public float dashStaminaCost = 25f;    // 一次冲刺消耗体力
    public float currentStamina;

    [Header("玩家数值 - 理智系统 / Player - Intelligence")]
    public float maxIntelligence = 100f;     // 最大理智
    public float currentIntelligence;

    [Header("玩家数值 - 生命 / Player - Health")]
    public int maxHealth;
    public int currentHealth;

    [Header("玩家数值 - 无敌时长 / Player - Invincibility")]
    public float invincibleTime;
    public float flashSpeed = 0.1f;


    [Header("敌人数值 - 战斗 / Enemy - Combat")]
    public int enemydamage;
    public float enemyAttackRange;
    public float enemyweaponRange;
    public float enemyattaCooldown;
    public float enemyknockbackForce;
    public float enemyknockbackTime;
    public float enemystunTime;

    [Header("敌人数值 - 移动 / Enemy - Movement")]
    public int enemyspeed;
    public float enemyplayerDetectRange = 5;

    [Header("敌人数值 - 生命 / Enemy - Health")]
    public int enemymaxHealth;

    [Header("敌人数值 - 子弹 / Enemy - Bullet")]
    public int shootDamage;
    public float enemyshootcd;
    public float bulletMaxLife;
    public float bulletspeed;
    public float enemyShootWaitTime;
    public float enemyshootRange;

    [Header("敌人数值 - 受伤闪烁 / Enemy - Hurt Flash")]
    public float enemyflashDuration = 0.3f;
    public float enemyflashSpeed = 0.08f;

    [Header("世界公共数值 / World Common")]
    public float slowDeceleration = 3f; // 减速加速度，越大停得越快
    public LayerMask playerLayer;
    public LayerMask enemyLayer;


    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
        }
    }
}
