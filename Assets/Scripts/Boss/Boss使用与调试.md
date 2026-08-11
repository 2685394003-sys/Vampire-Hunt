# BloodLord Boss 使用与调试

## 当前场景已经完成的配置

- `Visual`：已经承载 Boss 的 `SpriteRenderer` 与 `Animator`。
- `MeleePoint`：局部坐标 `(0, 0, 2)`，攻击 1 以它为圆心检测。
- `ProjectileOrigin`：局部坐标 `(0, 0.8, 1.2)`，攻击 2 从这里生成弹丸。
- `GroundIndicator`：圆形、十字和冲刺预警运行时生成在它下面。
- `VFXRoot`：每次攻击的红色反馈特效运行时生成在它下面。

这些挂点本来就应该是空 GameObject；它们主要提供位置和整洁的运行时层级，不需要分别挂五份脚本。

## 最快验证方法

1. 打开 `Assets/Scenes/New Scene.unity`。
2. 进入 Play 模式。
3. Game 窗口左上角会显示“Boss 运行时调试”。
4. 点击“攻击 1/2/3/4/6”可跳过阶段与冷却，直接验证每种攻击。
5. 点击“Boss -10 HP”验证受伤、阶段切换和死亡；也可点击“直接击杀 Boss”。
6. Scene 窗口选中 Boss 时，橙色圆是近战范围，青色点是弹丸起点，红色点是预警根节点，紫色框是特效根节点。

当前已关闭开局随机传送和阶段传送，防止竞技场中心未配置时 Boss 突然离开镜头。

## Animator

当前 Boss 没有专属动画素材，因此 `Animator Controller` 暂时为空。移动、攻击判定、伤害、阶段、弹丸和死亡逻辑仍可独立测试，调试面板会明确显示“未绑定 Controller”。

以后制作 Boss Animator Controller 时，参数名与类型应为：

- `Phase`：Int
- `PhaseChange`：Trigger
- `Death`：Trigger
- `Format1`、`Format2`、`Format3`、`Format4`、`Format5`、`Format6`：Trigger

如果美术使用其他命名，请在 `BossConfig` 的 Animator 参数区域同步修改。运行时会把缺失或类型错误写入 Console，不再静默忽略。

## 复用到新场景

在 Boss 根对象的 `BossController` 组件菜单中执行：

`Boss/自动配置五个子节点`

它会创建缺失节点、设置默认位置并完成引用。表现组件仍应放到 `Visual` 下。

## 与玩家脚本的边界

Boss 攻击只依赖 `IDamageable`、`IKnockbackReceiver` 和 `IForceKillable`。运行时的 `BossPlayerTargetAdapter` 负责兼容玩家现有的 `ChangeHealth`、`Knockback` 与 `ForceDeath`，因此玩家接口变化不会再让 Boss 核心脚本直接编译失败；适配失效时 Console 会明确报出缺少的方法。
