using UnityEngine;

namespace VampireHunt.Contracts
{
    /// <summary>
    /// 穿透消耗配置（全局唯一，所有武器共用）。
    /// 命中一个目标时按目标类型扣除该武器的 Pierce Count 额度，额度扣满即停止穿透。
    /// 由 LaserBeam（激光）与 SwordWaveEffect（剑气/自动步枪/狙击的弹丸）共同引用，
    /// 改这一个资产即可同时调整五把武器对 Boss 的阻挡强度，不需要逐个预制体改。
    /// </summary>
    [CreateAssetMenu(fileName = "PierceCostConfig",
        menuName = "Vampire Hunt/Combat/Pierce Cost Config")]
    public sealed class PierceCostConfig : ScriptableObject
    {
        [Header("穿透消耗（单位：穿透槽位）")]
        [Tooltip("普通敌人（近战/远程小怪）消耗的穿透数。")]
        [Min(1)] [SerializeField] private int normalPierceCost = 1;
        [Tooltip("Boss 本体消耗的穿透数。必须大于武器的 Pierce Count 才能挡住；否则该武器会打穿 Boss 继续命中后方目标。\n当前各武器 Pierce Count：步枪 5 / 激光 10 / 剑气 100 / 狙击 999。")]
        [Min(1)] [SerializeField] private int bossPierceCost = 30;
        [Tooltip("Boss 手消耗的穿透数。不想与本体区分时，保持与 Boss 本体相同即可。")]
        [Min(1)] [SerializeField] private int bossHandPierceCost = 30;

        public int NormalPierceCost => Mathf.Max(1, normalPierceCost);
        public int BossPierceCost => Mathf.Max(1, bossPierceCost);
        public int BossHandPierceCost => Mathf.Max(1, bossHandPierceCost);
    }
}
