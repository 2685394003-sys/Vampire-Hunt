using UnityEngine;

/// <summary>
/// 【示例】技能配置表 SO。
/// 继承 CSVConfigSO 自动获得列表存储 + GetById 查找。
/// </summary>
[CreateAssetMenu(fileName = "SkillConfig", menuName = "Config/技能配置表")]
public sealed class SkillConfig_SO : CSVConfigSO<SkillData>
{
}
