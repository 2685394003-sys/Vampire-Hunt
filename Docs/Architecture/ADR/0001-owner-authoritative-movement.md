# ADR 0001：玩家移动采用 Owner 客户端权威 + 服务器校验

> 状态：Adopted  
> 日期：2026-08-19  
> 关联章节：ARCHITECTURE §8.1、§12；MODULE_DESIGN §4

## 1. 问题

原规范把玩家移动纳入统一的“意图 → RPC → 服务器模拟 → 复制回客户端”流程（纯服务器权威）。对一个依赖走位、冲刺和 Boss 弹幕躲避的动作游戏，这会给本地操作引入一整个 RTT 的延迟，手感不可接受。

## 2. 决策

玩家**位置/朝向**采用 Owner 客户端权威（ClientNetworkTransform 或等价实现）：

- Owner 本地立即驱动位移与 Dash，位置直接复制到服务器与其他客户端。
- 服务器持有 `MovementValidationService`：校验位移速度上限（含 Dash 增益）、可行走区域、穿墙；违规时以服务器位置为准强制纠正并记录。
- 生命、伤害、暴击、奖励、技能、血契、刷怪、Boss 阶段**不受影响**，仍完全服务器权威。
- 以位置为输入的权威判定（近战命中、AOE 归属、刷怪距离）使用服务器上最近一次通过校验的位置。

## 3. 评估过的替代方案

| 方案 | 结论 |
|---|---|
| 纯服务器权威移动 | 操作延迟一个 RTT，动作手感不可接受；否决 |
| 服务器权威 + 客户端预测/和解 | 反作弊最强、手感好，但需要快照回滚与重放，实现和测试成本最高；当前合作 PvE 定位下收益不匹配，否决 |
| Owner 客户端权威 + 服务器校验（采纳） | NGO 标准做法，成本低、零输入延迟；反作弊强度略降，由服务器校验兜底 |

## 4. 影响与测试

- 影响：`PlayerController`/`Player Movement`/`PlayerDash` 的迁移目标改为 OwnerMovementMotor（客户端）+ MovementValidationService（服务器）；`MovePlayerCommand` 从命令契约中移除。
- 测试：Movement Validator 的速度上限/越界纠正（EditMode）；Owner 权威复制、超速回拉、Dedicated Server + 2 Clients 场景（Netcode 回归）。

## 5. 时效

非临时决策。若未来引入竞技性玩法需要升级为预测+和解模型，需新 ADR 取代本记录。
