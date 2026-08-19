# 重构第一阶段待修复清单

> 修复基线：`04d64b31`
> 审阅日期：2026-08-19  
> 范围：`Assets/Scripts/VampireHunt`、对应 asmdef、EditMode/Architecture Tests、Network Prefab 序列化迁移

## 1. 当前结论

第一阶段已经建立 16 个无环自研 asmdef，且 Domain/Application 没有直接引用 Unity 表现 API、NGO、TMP、Animator、Cinemachine 或场景查找器。RF-001 至 RF-010 已完成生产修复并通过既有 Unity EditMode 全量回归；RF-011、RF-012 的生产实现、配置资产和 Build Scene 接线已经落地，仍需完成 PlayMode 与真实多人进程矩阵，才能作为“运行时验收完成”。

本清单采用测试先行：能够稳定自动复现的问题已经先补回归测试，不使用 `[Ignore]`，生产实现修复前应保持红灯。组合根接线和多人网络行为无法仅靠 EditMode 单元测试证明，单列为集成验收项。

状态说明：

- `RED`：回归测试已加入，当前生产实现预期失败。
- `PENDING`：需要生产修复或补足可测试接口。
- `PARTIAL`：生产骨架已接入，仍缺场景资产或运行时矩阵证据。
- `MANUAL`：必须通过 Unity PlayMode、Prefab/Scene 或多人网络环境验证。
- `DONE`：实现、测试和运行时证据全部通过后才能标记。

## 2. 自动化修复项

| ID | 优先级 | 问题 | 已补测试 | 当前状态 | 修复验收条件 |
|---|---|---|---|---|---|
| RF-001 | P1 | Movement Validator 使用客户端 `ReportedAt` 差值生成移动预算，且首次上报可写入陈旧时间 | `MovementValidation_RejectsAnInitialPoseWithAStaleClientTimestamp`；`MovementValidation_RejectsStaleReportsBeforeGrantingDistanceBudget` | DONE | 保存服务器接收时间；校验 `now - MaximumReportAge <= ReportedAt <= now + MaximumFutureSkew`；速度预算不能仅由客户端时钟决定；补 Dash、越界和连续违规测试 |
| RF-002 | P1 | `PlayerVitals.IDamageReceiver` 入口固定使用 `now = 0`，首次受击后可能永久无敌 | `CombatApplication_UsesAuthoritativeTimeForPlayerInvincibility` | DONE | Combat 结算把权威服务器时间传入受击规则；无敌期结束后的伤害正常生效；无敌期内不产生零伤害表现事件 |
| RF-003 | P1 | Gameplay Effect 按 `SourceId` 批量删除修正器，移除一个效果会误删同源兄弟效果 | `RemovingOneEffect_PreservesOtherModifiersFromTheSameSource` | DONE | 每个效果实例拥有稳定句柄；移除时只删除该实例创建的修正器；同来源多效果互不干扰 |
| RF-004 | P1 | Attribute Execution 创建的修正器不属于 `Spec.Modifiers`，效果结束后可能泄漏 | `AttributeExecution_IsRemovedWhenItsOwningEffectExpires` | DONE | Execution 创建的持续修正器纳入效果所有权和清理流程；到期、Replace、Clear、回池均无残留 |
| RF-005 | P1 | Enemy Replicator 的全局 `tick` 按实体 Capture 次数递增，造成固定实体偏置或周期发送饥饿 | `FarEnemyReplication_GivesEveryEntityOnePeriodicSlotPerInterval` | DONE | 调度基于服务器复制帧或每实体最后发送时间；实体遍历顺序不影响频率；移动位置更新仍服从距离层级带宽策略 |
| RF-006 | P1 | Player/Enemy Aggregate 为 `public`，破坏“跨模块只公开 Contracts、Aggregate 默认 internal”边界 | `FeatureAggregates_AreInternalImplementationDetails` | DONE | Aggregate 改为 `internal`；测试通过 `InternalsVisibleTo` 或模块内 Factory；旧 Assembly-CSharp 无法直接写领域状态 |
| RF-007 | P1 | `Assets/Prefabs/Network/Player.prefab` 仍有两个已删除 `AttackDamageForwarder` 引用 | `NetworkPrefabs_HaveNoMissingScripts` | DONE | 使用 Unity 序列化移除缺失组件，或恢复兼容外壳后迁移；所有 Network Prefab 的 Missing Script 数量为 0 |
| RF-008 | P2 | Enemy 恢复状态采样“恢复点到目标”方向，而不是“当前位置到恢复点” | `EnemyRecovery_SamplesFromCurrentPositionTowardTheRecoveryCell` | DONE | Recovering 首先走向最近可行走格；到达后再恢复追击；覆盖边界外、障碍内和无恢复点三种情况 |
| RF-009 | P2 | `BloodPactOffer` 只暴露一个价格，但不同选项可按各自价格扣费 | `BloodPactSelection_ChargesThePriceAdvertisedByTheOffer` | DONE | Offer 为每个 Choice 携带权威价格，或强制同一 Offer 全部同价；UI 展示值与服务器实际扣费一致 |
| RF-010 | P2 | `AllowedDependencies` 未覆盖全部运行时 asmdef，未知程序集被静默跳过 | `RuntimeAssemblyDependencyPolicy_CoversEveryRuntimeAssembly` | DONE | 16 个运行时 asmdef 均有显式允许依赖；新增 asmdef 默认失败；GUID 引用解析后同样受规则约束 |

## 3. 集成与运行时修复项

### RF-011：组合根实际资产接线待验收（P1 / IMPLEMENTED + MANUAL）

现状：

- `NetworkRuntimeLauncher` 已进入非测试运行时启动链，并负责组合根的创建、唯一 Compose 与关闭释放。
- `DefaultCompositionFactoryProvider` 已安装 Gameplay、Offline/Netcode、Presentation、UI 的实际实现，并按固定模拟阶段推进。
- `RuntimeConfigCatalog`、Player/Enemy/Boss/Spawn/Blood Pact 配置资产已经建立；Editor 迁移器通过 Unity 序列化 API 接线 Build Scene。
- `SceneBindings`、三个 NGO RPC Adapter、唯一刷怪 Director、池适配器和运行时外壳绑定均已进入生产组合根。
- Dedicated Server 会跳过 Presentation/UI 安装；无界面真实进程推进仍属于手工运行矩阵。

修复验收：

1. 增加唯一 Runtime Launcher，在 Offline、Host、Dedicated Server 三种模式创建并释放组合根。
2. `SceneBindings`、`ConfigCatalog` 和模块 Factory 使用真实序列化资产接线；缺项启动即失败，不能空安装成功。
3. 旧 `PlayerNetworkState`、Enemy、Boss 外壳只保留输入、复制和委托职责。
4. PlayMode 证明移除任意 Presenter 不改变服务器伤害、奖励、刷怪和阶段结果。
5. Dedicated Server 在无 Camera、Animator、UI、Audio 时可以推进完整模拟。

建议后续补充的自动化入口：

- `CompositionRoot_RegistersRequiredGameplayServices`
- `RuntimeLauncher_ComposesExactlyOnceAndDisposesOnShutdown`
- `DedicatedServer_DoesNotCreatePresentationOrUiServices`
- `NetcodeMode_RejectsMissingRpcAndSpawnAdapters`

### RF-012：网络传输多人矩阵待验收（P1 / IMPLEMENTED + MANUAL）

现状：已建立版本化、强类型且 NGO 可序列化的 Command/State/Event Wire DTO，并增加实际 `NetworkBehaviour` RPC 适配器、服务端 sender/owner 校验、状态顺序保护和瞬时事件去重。尚未执行 Host + 1 Client、Dedicated Server + 2 Clients 与 Late Join 的真实进程矩阵，因此不能声明多人传输验收完成。

修复验收：

1. 为 Command、State DTO 和 GameplayEvent 建立明确的 NGO 可序列化协议与版本策略。
2. 所有命令在服务器验证 sender/owner；客户端不能伪造其他玩家的 Pose、Dash、Attack 或血契选择。
3. Host + 1 Client、Dedicated Server + 2 Clients 覆盖移动纠正、一次性伤害、奖励归属、Late Join 和对象池重置。
4. 瞬时事件与状态快照任意先后到达时，Presenter 结果一致且不会重复表现。

## 4. 测试执行分组

迁移前一轮 Unity EditMode 基线结果：70/70 通过，0 Failed，0 Ignored。最终迁移又增加了 Player/Enemy/Boss Runtime、模拟阶段顺序、首波延迟、池生命周期、PlayMode 与 Netcode 回归用例；这些新增用例必须以本次最终 Unity Test Runner 结果为准，不能沿用旧的 70/70 数字。重点覆盖包括：

- `VampireHunt.Player.EditMode.Tests`：4 个。
- `VampireHunt.Modules.Tests` / Abilities、Navigation：3 个。
- `VampireHunt.Infrastructure.EditMode.Tests`：1 个。
- `VampireHunt.Architecture.Tests`：3 个。

执行顺序：

1. Unity Test Runner 执行全部 EditMode Tests，确认上述测试能够稳定复现红灯。
2. 每次修复只把对应 ID 的测试转绿，不删除断言、不改成 `[Ignore]`。
3. 全部 EditMode 绿灯后执行 PlayMode。
4. 最后执行 Host、Dedicated Server 和目标敌人数量下的网络/性能回归。

外部 `dotnet test` 不是本项目 Unity Test Runner 的替代证据；Unity 生成的测试 csproj 可能受 NUnit 与目标框架版本差异影响。最终验收必须保留 Unity Test Runner 结果和多人运行时证据。

## 5. 完成定义

本清单只有同时满足以下条件才可关闭：

- RF-001 至 RF-010 的回归测试全部通过。
- 16 个运行时项目零编译错误，架构依赖图无环且策略全覆盖。
- Build Scene、Network Prefab、Resources、Addressables 和 Builder 输出零 Missing Script。
- RF-011、RF-012 的 PlayMode 与多人网络矩阵通过。
- 生产代码中不存在未记录的反向依赖、客户端权威玩法写入或 Presenter 修改领域状态。
- 修复没有破坏现有 Prefab GUID、NetworkObject、Spawn Config 和默认敌人接线。
