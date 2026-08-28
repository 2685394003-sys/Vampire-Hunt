using UnityEngine;
using UnityEngine.AI;
using Blocks.Gameplay.Core;
using VampireHunt.Infrastructure.Netcode;

namespace VampireHunt.Systems
{
    /// <summary>
    /// 死亡重生位置解析：以 Boss 为圆心，在环带（50~60m）内随机取空地，用 NavMesh 采样避免卡进墙体。
    /// 通过 <see cref="GameManager.ResolveRespawnPosition"/> 注入点接入 Core 的重生流程，不修改 Core 的 GameManager。
    /// </summary>
    public static class BossCentricRespawn
    {
        // ── 重生环带参数（以 Boss 为圆心）──
        /// <summary>重生点距 Boss 的最小半径（米）。</summary>
        public const float MinRadius = 50f;
        /// <summary>重生点距 Boss 的最大半径（米）。</summary>
        public const float MaxRadius = 60f;
        /// <summary>NavMesh 采样距离（米）：候选点投影到最近可行走面的最大容差。</summary>
        public const float NavMeshSampleDistance = 5f;
        /// <summary>随机取点尝试次数（全部失败则回退默认重生点）。</summary>
        public const int SampleAttempts = 24;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            GameManager.ResolveRespawnPosition = TryResolve;
        }

        private static bool TryResolve(out Vector3 position)
        {
            position = Vector3.zero;

            var boss = Object.FindAnyObjectByType<BossEncounterDirector>();
            if (boss == null)
            {
                Debug.LogWarning("[BossCentricRespawn] 未找到 Boss，回退到默认重生点。");
                return false;
            }

            Vector3 center = boss.transform.position;
            for (int i = 0; i < SampleAttempts; i++)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Random.Range(MinRadius, MaxRadius);
                Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                // NavMesh.SamplePosition 命中的是可行走面（墙体内无 NavMesh），天然避开墙体
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavMeshSampleDistance, NavMesh.AllAreas))
                {
                    position = hit.position;
                    return true;
                }
            }

            Debug.LogWarning("[BossCentricRespawn] 未能在 Boss 周围找到空地，回退到默认重生点。");
            return false;
        }
    }
}
