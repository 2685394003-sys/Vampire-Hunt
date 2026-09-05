# Boss 多人技能数值规则

## 分层位置

```text
BossAbilityAsset（Data，只保存四项系数）
└─ BossAbilityMultiplayerScaling
   ├─ DamageCoefficient
   ├─ QuantityCoefficient
   ├─ TelegraphCoefficient
   └─ CooldownCoefficient

BossAbilityServerDriver（Netcode，只在服务器统计已生成 PlayerObject 的客户端）
└─ BossAbilityController（纯 C#，施法开始时冻结人数与最终倍率）
   ├─ BossAbilityCastContext（技能逻辑读取）
   └─ BossAbilitySnapshot → BossAbilityStateReplicator（客户端时间轴读取）
```

人数只在一次技能开始时采样。施法中途加入或断开的玩家不会改变这次技能，下一次施法才使用新人数。这样服务器伤害判定、冷却与所有客户端预警不会在半途发生分歧。

## 默认公式

- 单人：无论系数填多少，都保持原始伤害、数量、前摇和 CD。
- 多人：`最终倍率 = 施法开始时玩家数 × 对应系数`。
- 伤害：技能运行时使用配置伤害的副本，不修改 ScriptableObject 资产。
- 前摇：只拉伸 Telegraph；Resolve 与 Recover 保持原播放速度。表现 Cue 会映射到服务器同步后的 Telegraph 时间轴。
- CD：服务器调度器使用本次有效前摇和有效 CD 计算下一次可用时间。
- 激光：默认每名玩家一束，服务器尽量给存活玩家分配不同光束；目标不足时循环分配。
- 旋转弹幕：单人保持 2 臂，多人使用 `1 + 数量单位`，默认即 `1 + 玩家数`；波数不变，每波方向等角度分布。

为防止错误配置或异常连接数导致无限生成，人数、数量单位和激光束数上限均为 64。

## 网络职责

- 服务器：人数采样、倍率计算、选目标、弹体生成、激光旋转、范围判定、伤害、冷却。
- 客户端：读取 `BossAbilityNetworkState` 的有效前摇和人数，只播放预警、音效、动画和 VFX。
- 多束激光：每束具有稳定的 `BeamIndex`；服务器在释放瞬间发送一次朝向快照，释放期间以 20 Hz 上限同步朝向。客户端只做视觉插值，不参与命中判定。

项目架构目前不正式支持战斗中途加入；已连接并拥有已生成 `PlayerObject` 的 Host/Client 会收到本次技能广播。

## 手动检查建议

1. 单人 Host 强制释放弹幕和激光，确认仍为 2 臂、1 束，原伤害/前摇/CD 不变。
2. Host + 1 Client 时确认弹幕为 3 臂、激光为 2 束，两个窗口的预警起止时间一致。
3. 两名玩家同时站入同一束或多束激光，确认服务器每个玩家仍按技能的命中间隔受伤，不会因光束重叠在同一帧重复结算。
4. 施法中断开一个 Client，确认本次技能参数不突变；下一次技能才按新人数计算。
