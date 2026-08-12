using System;
using UnityEngine;

/// <summary>
/// 玩家属性数据，字段与 CSV 表头一一对应。
/// 由 PlayerStatsCSVImporter 从 CSV 自动填充。
/// </summary>
[Serializable]
public class PlayerStatsData
{
    [Header("标识 / Identity")]
    public string playerId;

    [Header("生命与体力 / Health & Stamina")]
    public float maxHealth;
    public float maxStamina;
    public float dashStaminaCost;
    public float staminaRecoverSpeed;

    [Header("战斗 / Combat")]
    public float damage;
    public float weaponRange;
    public float cooldown;
    public float knockbackForce;
    public float critRate;
    public float critDamage;

    [Header("移动 / Movement")]
    public float speed;
    public float dashSpeedMultiplier;
    public float dashDuration;

    [Header("受击 / Hit Reaction")]
    public float invincibleTime;
    public float flashSpeed;
    public float knockbackTime;
    public float stunTime;

    [Header("资源 / Resources")]
    public float maxScarlet;
    public float maxIntelligence;
}
