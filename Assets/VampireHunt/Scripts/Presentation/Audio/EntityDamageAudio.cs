using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Presentation.Audio
{
    /// <summary>
    /// 实体受伤音效（非侵入式呈现组件，与 CombatVfxPresenter 同模式）。
    /// 挂到玩家/敌人/Boss 的 GameObject 上，订阅全局伤害呈现事件，
    /// 仅当受伤目标是自己时向 Wwise 发指定事件；不修改任何战斗逻辑。
    /// 事件名对应《策划版音频调用表_demo.xlsx》：玩家填 Play_Player_Hurt，敌人填 Play_Enemy_Hurt，Boss 填 Play_Boss_Hurt。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntityDamageAudio : MonoBehaviour
    {
        [SerializeField] private DamagePresentationEvent onDamage;
        [Tooltip("本实体受伤时触发的 Wwise 事件名。")]
        [SerializeField] private string damageEventName = "Play_Player_Hurt";

        private ICombatEntityIdentity m_Identity;

        private void Awake()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length && m_Identity == null; i++)
            {
                if (behaviours[i] is ICombatEntityIdentity identity) m_Identity = identity;
            }
        }

        private void OnEnable()
        {
            onDamage?.RegisterListener(HandleDamage);
        }

        private void OnDisable()
        {
            onDamage?.UnregisterListener(HandleDamage);
        }

        private void HandleDamage(DamagePresentationPayload payload)
        {
            if (payload.targetEntityId == 0 || m_Identity == null) return;
            if (m_Identity.CombatEntityId.Value != payload.targetEntityId) return;
            WwiseAudioBridge.PostEvent(damageEventName, gameObject);
        }
    }
}
