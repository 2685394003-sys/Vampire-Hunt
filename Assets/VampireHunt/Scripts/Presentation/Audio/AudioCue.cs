using Blocks.Gameplay.Core;
using UnityEngine;

namespace VampireHunt.Presentation.Audio
{
    /// <summary>
    /// 统一发声入口：把「事件名为空」与「target 为空」两个守卫收在一处，调用点只写一行。
    /// 数据驱动槽（<c>[SerializeField] string xxxEventName</c>）留空时静默跳过 —— 不报错、不发声，
    /// 因此「槽还没填值」不会污染 Console，也不会挡住其它表现。
    /// </summary>
    /// <example>
    /// <c>AudioCue.Post(m_AttackEventName, gameObject);</c>
    /// </example>
    public static class AudioCue
    {
        public static void Post(string eventName, GameObject target)
        {
            if (string.IsNullOrEmpty(eventName) || target == null) return;
            WwiseAudioBridge.PostEvent(eventName, target);
        }
    }
}
