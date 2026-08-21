# 运行时代码永久迁移计划

> 基线：`ARCHITECTURE.md`（Adopted）与 `MODULE_DESIGN.md`  
> 原则：迁移结果直接写入代码、Prefab、Scene 与配置资产；运行时和编译后均不依赖自动迁移工具。

## 1. 不变量

- 保留现有 Prefab、`NetworkObject`、`.meta` GUID 与序列化引用，采用“保留外壳、移动职责、切换调用、验证行为、最后删除外壳”。
- Domain/Application 不依赖 Unity 表现、场景查找、NGO 或具体 MonoBehaviour。
- Player 的位置与朝向保持 Owner 权威并由服务器校验；生命、伤害、奖励、AI 与刷怪保持服务器权威。
- 全场只有一个 `EnemySpawnDirector` 权威入口；旧刷怪器只能成为适配器或被禁用，不能并行推进预算。
- Scene/Prefab 的最终状态由资产测试守卫，不允许 `[InitializeOnLoad]` 或编译后回写脚本作为正确性的前提。
- `Assets/Scripts` 下所有 `.cs` 必须受最近的 asmdef 管辖；禁止回落或引用 `Assembly-CSharp`。
- 未拆完的旧 Unity 外壳只允许位于最外层 `VampireHunt.Legacy` 适配器程序集，模块程序集禁止反向引用它。

## 2. 执行顺序与文件所有权

| 阶段 | 所有者 | 范围 | 完成条件 |
|---|---|---|---|
| M0 永久化基线 | 主代理 | Editor 工具、Prefab/Scene 守卫、组合根 | 移除专用自动迁移脚本；旧 authoring utility 不再自动运行；永久序列化引用有测试 |
| M1 Player | Player 迁移工作单元 | `Scripts/Player`、Player Domain/Application、Input adapters | `PlayerController` 等只保留 Unity/Prefab/NGO 壳，规则经 Command/Result/Event/ReadModel 进入新框架 |
| M2 Navigation + Enemies | Enemy/Navigation 工作单元 | `Scripts/Enemy`、`FlowFieldManager`、Navigation、Enemies | 纯流场算法只有 Domain 权威实现；Enemy MonoBehaviour 只负责 motor/presentation/replication |
| M3 Spawning + Netcode | Spawning/Netcode 工作单元 | Spawn Domain、NGO adapters、runtime spawn bootstrap | 单一 server-authoritative director；池、RPC、状态与事件复制均经端口适配 |
| M4 Shared rules | 后续迁移工作单元 | `GameplayAbilities`、Stats、Combat | 遗留 GAS/属性/伤害规则委托纯 C# Domain/Application；NetworkBehaviour 只复制状态和事件 |
| M5 Boss | 后续迁移工作单元 | `Scripts/Boss`、Boss Domain/Application/Adapters | Boss 状态机、攻击选择与阶段规则只有新框架权威实现，Prefab 外壳保持兼容 |
| M6 Presentation + UI | 后续迁移工作单元 | `Scripts/UI`、BGM、Camera、各 Presenter | Presenter 仅消费 GameplayEvent/ReadModel；音频/相机/动画缺失不影响模拟 |
| M7 组合与资产收口 | 主代理 | Contracts、Bootstrap、Prefabs、Scenes、Builders、asmdef | `GameCompositionRoot` 是唯一对象图入口；序列化链和 NetworkPrefab GUID 保持稳定；Scripts 零 Assembly-CSharp |
| M8 验收 | 主代理 | Architecture/EditMode/PlayMode/Netcode | 编译零错误；架构测试通过；Host/Client 与 Dedicated Server 关键链验证并记录证据 |

## 3. 分批删除门槛

遗留外壳只有同时满足以下条件后才可删除：

1. 所有调用方已经切到新 Contract/Application 入口。
2. Prefab/Scene/Builder 不再序列化该类型，并通过零 Missing Script 检查。
3. 对应 Domain 规则、PlayMode 接线和网络权威行为已有回归测试。
4. Host + Client 以及 Dedicated Server + Clients 的关键行为验证通过。

## 4. 每批验收清单

- 依赖方向与 Contracts namespace 守卫通过。
- Dedicated Server 不依赖 Animator、UI、Audio、Camera 推进逻辑。
- 表现只消费 Result、GameplayEvent 或 ReadModel。
- Prefab/Scene/Builder 同步更新且 `.meta` GUID 不变。
- 不出现并行旧/新权威路径、隐藏场景查找或新增 Singleton。
- 未完成的真实多人验收明确保留为开放项，不以静态测试替代。
