# Boss 运行时 API 与 3D 接入

## 1. 架构边界

- `IBossController`：玩家、NPC、UI、关卡脚本唯一应依赖的 Boss 门面。
- `BossRegistry`：在不持有场景引用时查找主 Boss 或最近 Boss。
- `BossBehaviorPolicy`：追猎/战斗移动决策；不持有 Unity 生命周期。
- `BossAttackController`：攻击选择、预警、判定与冷却。
- `Boss3DAnimationPresenter`：只消费门面事件，通过 Playables 播放 Humanoid 动画；不控制位移和伤害。
- `BossHealth`：服务器权威的生命、阶段、无敌与死亡状态。

外部代码不要直接改 `BossHealth` 字段、移动刚体或调用攻击协程。

## 2. 查找 Boss

```csharp
if (BossRegistry.TryGetPrimary(out IBossController boss))
{
    BossSnapshot snapshot = boss.Snapshot;
    Debug.Log($"Boss HP: {snapshot.CurrentHealth}/{snapshot.MaxHealth}");
}
```

查找离 NPC 最近的 Boss：

```csharp
if (BossRegistry.TryGetClosest(transform.position, out IBossController boss))
{
    Vector3 direction = boss.ActorTransform.position - transform.position;
}
```

## 3. 常用命令

所有写命令只允许服务器或离线模式执行，并返回 `BossCommandResult`。调用方必须检查结果。

```csharp
BossCommandResult targetResult = boss.AssignTarget(playerTransform);
BossCommandResult startResult = boss.StartCombat();
BossCommandResult modeResult = boss.SetEncounterMode(BossEncounterMode.Hunt);
BossCommandResult damageResult = boss.ApplyDamage(10, attacker.position);
BossCommandResult attackResult = boss.TryForceAttack(BossAttackType.Format2);
```

### 追猎与 Boss 战

- `Hunt`：Boss 在镜头外原地待机攻击；玩家进入接近距离后，Boss 面朝玩家并按玩家移动速度后退；拉开到安全距离后停止。
- `Battle`：Boss 使用原有竞技场接近/驻足和阶段攻击逻辑。

```csharp
boss.SetEncounterMode(BossEncounterMode.Battle);
```

### 猩红踉跄与处决

猩红系统达到小阶段阈值后，先调用 `RequestStagger()`。只有目标在 `BossConfig.staggerActivationDistance` 内才会成功。Boss 会先随机释放当前阶段允许的前置招式，再打开脆弱窗口。

```csharp
BossCommandResult result = boss.RequestStagger();
if (result == BossCommandResult.Succeeded)
{
    // 等待 StaggerStateChanged => Vulnerable，再播放玩家处决演出。
}
```

处决命中时：

```csharp
BossCommandResult result = boss.ExecuteStagger(
    damage: 10,
    executor: playerTransform);
```

掉落魔币、猩红资源和阶段累计属于外部奖励系统。奖励系统订阅 `StaggerExecuted`，不要让 Boss 核心反向依赖玩家背包或货币实现。

## 4. 事件

- `StateChanged`：移动/攻击/阶段/死亡主状态变化。
- `EncounterModeChanged`：`Hunt` 与 `Battle` 切换。
- `StaggerStateChanged`：`None -> Telegraph -> Vulnerable -> Executed/None`。
- `StaggerExecuted`：处决已被服务器接受；奖励系统入口。
- `AttackStarted / AttackCompleted / AttackCancelled`：表现、音效和 AI 观察入口。
- `SnapshotChanged`：UI 刷新入口。
- `Defeated`：战斗结算入口。

订阅者必须在自身 `OnDisable` 中解除订阅。

## 5. 3D 动画接入规范

模型：

`Assets/Art/Characters/reimi_black/Re_reimi_black.fbx`

该模型和 Sword and Shield Pack 当前均为 Humanoid，可进行 Avatar 重定向。`Boss3DAnimationPresenter` 禁止 Root Motion，位移始终由服务器权威的 Boss Motor 控制。动画命令通过 `NetworkVariable` 带序号同步，连续播放相同招式也不会被客户端吞掉。

建议片段映射：

| Presenter 字段 | 动画资源 |
| --- | --- |
| Idle | `Idle.anim` |
| Run | `run.anim` |
| Retreat | `walk.anim` |
| Phase Change | `sword and shield power up.fbx` |
| Stagger | `sword and shield impact.fbx` |
| Execution Impact | `sword and shield impact (2).fbx` |
| Death | `sword and shield death.fbx` |
| Format1 Sweep | `slash.anim` |
| Format2 Barrage | `sword and shield casting.fbx` |
| Format3 Cross Slash | `sword and shield slash (2).fbx` |
| Format4 Charged Slash | `sword and shield attack (4).fbx` |
| Format6 Dash | `sword and shield run.fbx` |

在 `Boss3DAnimationPresenter` 组件菜单执行“Boss/验证 3D 动画配置”，可以一次性检查 Humanoid Avatar 和所有必需片段。

## 6. 场景与联机资源

`Assets/Scenes/SampleScene.unity` 已直接放置 `Boss_BloodLord` 3D 对象，编辑模式即可调整模型、CapsuleCollider、护卫判定体和四个攻击挂点。Boss 本体、模型和核心挂点不会在运行时补建。

格式 2 的 `BossProjectile_3D.prefab` 位于 Boss 文件夹内，并由场景根节点上的 `BossNetworkPrefabRegistrar` 在网络会话启动前注册。它的 Transform 由 `NetworkTransform` 同步，命中与伤害仍由服务器权威处理。

当前模型与动画包的 Humanoid Avatar 链路已经成立；循环由 Playables 表现层显式处理，不需要改模型或动画导入器，也不需要新建 Animator Controller。
