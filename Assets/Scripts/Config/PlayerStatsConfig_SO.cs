using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家属性配置表（ScriptableObject）。
/// 由 Tools/导入玩家数据表CSV 自动生成，运行时只读。
/// </summary>
[CreateAssetMenu(fileName = "PlayerStatsConfig", menuName = "Config/玩家属性配置表")]
public sealed class PlayerStatsConfig_SO : ScriptableObject
{
    [SerializeField] private List<PlayerStatsData> players = new List<PlayerStatsData>();

    /// <summary>所有玩家配置的只读列表。</summary>
    public IReadOnlyList<PlayerStatsData> Players => players;

    /// <summary>
    /// 按 playerId 查找配置。找不到返回 null。
    /// </summary>
    public PlayerStatsData GetById(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return null;
        foreach (PlayerStatsData p in players)
        {
            if (p != null && p.playerId == playerId) return p;
        }
        return null;
    }

    /// <summary>导入工具内部使用：清空并替换全部数据。</summary>
    public void SetAll(List<PlayerStatsData> data)
    {
        players.Clear();
        if (data != null) players.AddRange(data);
    }
}
