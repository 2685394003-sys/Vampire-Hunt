# BloodLord 3D Boss 使用与调试

## 场景配置

`Assets/Scenes/SampleScene.unity` 中的 `Boss_BloodLord` 是可在编辑模式直接查看和调试的 3D 场景对象，不会在进入游戏后才生成 Boss 本体。

- `Visual/Re_reimi_black_Model`：`Re_reimi_black.fbx` 模型与 Humanoid Animator。
- `MeleePoint`：近战攻击判定起点。
- `ProjectileOrigin`：弹幕生成点。
- `GroundIndicator`：地面预警和阶段血池的分类节点。
- `VFXRoot`：攻击反馈、契约吸取线与三阶段血雨的分类节点。
- `Guard_Left_Hitbox` / `Guard_Right_Hitbox`：格式 1 使用的左右护卫判定体。

Boss 根对象已显式挂载 `BossConfig`、`BossHealth`、`BossController`、`BossAttackController`、`Boss3DAnimationPresenter`、`BossNetworkPrefabRegistrar` 和 `NetworkObject`。模型、碰撞体、挂点及动画引用均保存在场景中；运行时只会按招式生成弹道、预警与短生命周期特效。

格式 2 使用 `Assets/Scripts/Boss/Prefabs/BossProjectile_3D.prefab`。它带有 `NetworkObject`、`NetworkTransform`、3D 触发碰撞体和 `BossProjectile`；`BossNetworkPrefabRegistrar` 会在 Host/Client 启动前由每个场景实例确定性注册，不需要改项目公共的 NetworkPrefabsList。

## 3D 动画

`Boss3DAnimationPresenter` 通过 Playables 播放 `Sword and Shield Pack` 的 Humanoid 动画，不依赖 Animator Controller 状态名。当前映射覆盖：

- Idle、Run、Retreat
- PhaseChange、Stagger、ExecutionImpact、Death
- Format1、Format2、Format3、Format4、Format6

Root Motion 已关闭，Boss 位移始终由服务器权威的移动层控制。

## 调试

1. 打开 `Assets/Scenes/SampleScene.unity`，在层级中选择 `Boss_BloodLord`，可直接调整位置、碰撞体和挂点。
2. 进入 Play 模式，使用 Game 窗口中的 Boss 调试面板测试攻击、受伤、阶段切换和死亡。
3. 玩家与 NPC 应通过 `IBossController` / `BossRegistry` 调用 Boss，接口示例见 `Boss_API_CN.md`。
4. 若需要重新安装或更新场景对象，使用 Unity 菜单：`Vampire Hunt > Boss > 安装或更新当前场景的 3D Boss`。

## 约束

- 不允许运行时补建 BossController、模型、主碰撞体或五个核心挂点；缺失时会明确报配置错误。
- 玩家、NPC、UI 与奖励系统不应直接依赖 Boss 内部协程或字段。
- Boss 伤害边界为 `IDamageable`、`IKnockbackReceiver`、`IForceKillable`。
