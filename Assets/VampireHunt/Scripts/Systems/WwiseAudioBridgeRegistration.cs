using UnityEngine;
using Blocks.Gameplay.Core;

namespace VampireHunt.Systems
{
    /// <summary>
    /// 启动时把 Wwise 的 AkSoundEngine.PostEvent 注入到 Core 的 WwiseAudioBridge。
    /// 与 BossCentricRespawn 同模式：Core 扩展点 + 上层注入，Core 不直接依赖 Wwise。
    /// </summary>
    public static class WwiseAudioBridgeRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            WwiseAudioBridge.Post = (eventName, target) =>
            {
                if (target == null)
                {
                    return;
                }
                AkSoundEngine.PostEvent(eventName, target);
            };

            // 加载音效拆分的 bank（事件按《策划版音频调用表》归属）：
            //   玩家事件 → Bank_Player；敌人/Boss 事件 → Bank_Combat；UI 事件 → Init（Wwise 自动加载，无需 LoadBank）。
            // ⚠️ 音效同学在 Wwise 里建的 SoundBank 名必须与此处字符串完全一致，否则加载失败（静默无声）。
            AkBankManager.LoadBank("Bank_Player", false, false);
            AkBankManager.LoadBank("Bank_Combat", false, false);
        }
    }
}
