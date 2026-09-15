using UnityEngine;

namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// Wwise 音频桥（Core 扩展点）：Core 不直接依赖 Wwise 程序集。
    /// 事件名以《策划版音频调用表_demo.xlsx》的「Event 名称」列为契约；
    /// 由上层（VampireHunt.WwiseAudioBridgeRegistration）在启动时注入 AkSoundEngine 实现。
    /// </summary>
    public static class WwiseAudioBridge
    {
        public delegate void PostEventDelegate(string eventName, GameObject target);

        /// <summary>由上层注入的真实 PostEvent 实现；未注入时静默（不报错、不发声）。</summary>
        public static PostEventDelegate Post;

        /// <summary>安全的发布入口：未注入实现时静默跳过。</summary>
        public static void PostEvent(string eventName, GameObject target)
        {
            Post?.Invoke(eventName, target);
        }
    }
}
