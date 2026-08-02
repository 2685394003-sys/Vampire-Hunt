using UnityEngine;

/// <summary>
/// 生成方向对准 Boss(SpawnPointsAimBoss)。
/// 挂在 shengchengfangxiang(生成方向父物体)上:
/// 每帧绕 Y 轴旋转自身,让"生成点多的那一面"(denseLocalDirection 本地方向)始终朝向 boss,
/// 使大部分怪物从 boss 方向涌来(设计:玩家靠怪物来向找 boss)。
/// 只旋转自己,不影响 Player;6 个刷怪点子物体随父物体一起转。
/// </summary>
[DisallowMultipleComponent]
public class SpawnPointsAimBoss : MonoBehaviour
{
    [Header("对准目标 (Target)")]
    [Tooltip("boss 的 Transform;留空自动查找场景中的 BossHealth")]
    public Transform boss;

    [Header("密集面方向 (Dense Side)")]
    [Tooltip("生成点较多的那一面在本地空间的方向(仅水平分量生效)。当前布局西侧(-X)有 1/5/6 三个点,默认 left")]
    public Vector3 denseLocalDirection = Vector3.left;

    [Tooltip("旋转速度(度/秒);0 = 瞬间对准")]
    [Min(0f)] public float rotationSpeed = 360f;

    [Header("Boss 查找")]
    [Tooltip("boss 引用丢失时的重新查找间隔(秒)")]
    [Min(0.1f)] public float findRetryInterval = 0.5f;

    [Header("Gizmo")]
    public float gizmoArrowLength = 5f;

    private float findTimer;
    private bool bossLogged;           // 锁定日志只打一次

    private void LateUpdate()
    {
        if (boss == null)
        {
            findTimer -= Time.deltaTime;
            if (findTimer > 0f) return;
            findTimer = findRetryInterval;
            // 先找激活的 boss;找不到再用 FindObjectsOfTypeAll 连禁用的也找(boss 可能被暂时禁用)
            // 注意:6000.5 的泛型 FindFirstObjectByType 没有 (FindObjectsInactive, FindObjectsSortMode) 双参重载(CS1501),勿用
            BossHealth bh = FindFirstObjectByType<BossHealth>();
            if (bh == null)
            {
                // FindObjectsOfTypeAll 能找到禁用物体,但也会带出 prefab 资产;
                // 用 scene.IsValid() 过滤,只保留场景里的物体
                foreach (BossHealth cand in Resources.FindObjectsOfTypeAll<BossHealth>())
                {
                    if (cand != null && cand.gameObject.scene.IsValid())
                    {
                        bh = cand;
                        break;
                    }
                }
            }
            if (bh != null)
            {
                boss = bh.transform;
                if (!bossLogged)
                {
                    bossLogged = true;
                    Debug.Log("[生成方向] 已锁定 boss:" + bh.name + (bh.gameObject.activeInHierarchy ? "(激活)" : "(当前禁用,对准其停驻位置)"), this);
                }
            }
            if (boss == null) return; // 场景没有 boss(或已销毁):保持当前朝向
        }

        // boss 方向(水平)
        Vector3 toBoss = boss.position - transform.position;
        toBoss.y = 0f;
        if (toBoss.sqrMagnitude < 0.001f) return;
        toBoss.Normalize();

        Vector3 dense = denseLocalDirection;
        dense.y = 0f;
        if (dense.sqrMagnitude < 0.001f) dense = Vector3.left;
        dense.Normalize();

        // 目标旋转:让本地 dense 方向转到 boss 方向
        // target * dense == toBoss (验证:LookRotation(toBoss) * Inverse(LookRotation(dense)) * dense = toBoss)
        Quaternion target = Quaternion.LookRotation(toBoss) * Quaternion.Inverse(Quaternion.LookRotation(dense));

        if (rotationSpeed <= 0f)
        {
            transform.rotation = target;
        }
        else
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * Time.deltaTime);
        }
    }

    private void OnDrawGizmos()
    {
        // 红色箭头 = 密集面当前朝向(编辑时始终可见)
        Vector3 dense = denseLocalDirection;
        dense.y = 0f;
        if (dense.sqrMagnitude < 0.001f) dense = Vector3.left;
        dense.Normalize();
        Vector3 worldDir = transform.TransformDirection(dense);
        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.95f);
        Gizmos.DrawRay(transform.position, worldDir * gizmoArrowLength);
        Gizmos.DrawWireSphere(transform.position + worldDir * gizmoArrowLength, 0.4f);
    }
}
