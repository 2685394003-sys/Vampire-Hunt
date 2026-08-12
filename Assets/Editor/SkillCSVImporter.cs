using UnityEditor;

/// <summary>
/// 【示例】技能表 CSV 导入器。
/// 继承 CSVImporterBase，只需指定类型、路径和菜单名，核心逻辑全部复用。
/// </summary>
public sealed class SkillCSVImporter : CSVImporterBase<SkillData, SkillConfig_SO>
{
    protected override string OutputPath => "Assets/Config/SkillConfig.asset";
    protected override string DataLabel => "技能数据";

    [MenuItem("Tools/导入技能表CSV")]
    public static void Import()
    {
        new SkillCSVImporter().RunImport();
    }
}
