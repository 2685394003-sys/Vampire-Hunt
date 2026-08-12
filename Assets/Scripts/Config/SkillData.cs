using System;
using UnityEngine;

/// <summary>
/// 【示例】技能数据。
/// 演示如何用通用 CSV 导入系统添加一张新表。
/// 字段名必须与 CSV 表头一致。
/// </summary>
[Serializable]
public class SkillData
{
    public string skillId;
    public string skillName;
    public float damage;
    public float cooldown;
    public float range;
    public string description;
}
