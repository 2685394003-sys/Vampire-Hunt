# BloodLord Boss 使用与调试

## 场景已经配置好的对象

`Assets/Scenes/New Scene.unity` 中的 `Boss_BloodLord` 已完成连线。

- `Visual`：Boss 本体立绘、SpriteRenderer、Animator。
- `MeleePoint`：格式1渐进横扫的起点。
- `ProjectileOrigin`：格式2旋转弹幕的生成点。
- `GroundIndicator`：所有地面预警、阶段血池的父节点。
- `VFXRoot`：攻击反馈、格式5吸取线、三阶段血雨的父节点。
- `Guard_Left` / `Guard_Right`：左右护卫，各有独立生命与碰撞体。

前五个节点本来就应该是空 GameObject。它们是“挂点”和分类节点，不需要分别挂脚本；攻击运行时会自动在对应节点下生成预警或特效。

## 面向摄像机与移动镜像

Boss 本体和两只护卫都挂有 `BossSpriteFacing`：

- 立绘每帧与摄像机平面保持平行。
- 只有产生实际位移时才重新判断左右镜像。
- 向屏幕左侧移动时 `flipX=true`，向右移动时恢复；停止后保留最后朝向。
- 阶段随机传送不会被误判成普通移动，因此不会因为瞬移突然翻面。

护卫在 Play 模式开始时会脱离 Boss 根节点，沿摄像机屏幕左右分别保持编队位置，并以自己的实际位移独立计算镜像。Boss 根节点旋转或移动不会直接强制翻转护卫立绘。

## 当前招式

- 格式1：护卫渐进横扫。双护卫完好时左右各扫一次；一只受损后禁用对应方向；两只都受损后不再选择格式1。
- 格式2：以 Boss 为中心向外发射四臂旋转弹幕。
- 格式3：二阶段解锁。Boss 脚下圆形由透明逐渐变红，同时显示两条红色矩形十字框和半透明判定区域；蓄力结束后才造成伤害，不击退。
- 格式4：一阶段解锁，读取玩家蓄力开始时的位置，生成矩形全屏斩预警并判定伤害。
- 格式5：生命低于 20% 仅触发一次，显示玩家到 Boss 的吸取线，并让契约倒计时以 2 倍速减少。
- 格式6：三阶段解锁，读取玩家方向后显示红色长方形，Boss 沿路径冲刺；持续时间内进入区域会按间隔重复受伤。

阶段阈值为 70%、30%、10%。阶段切换会停止移动、清理附近障碍、击退玩家、短暂子弹时间、闪烁并随机传送。进入二、三阶段会生成可拾取血池；进入三阶段会启动血雨。

## 高度规则

`BossConfig.minimumEffectHeight` 当前为 `-0.99`。所有地面预警、弹丸、吸取线、血池和攻击反馈都会通过统一函数钳制，保证世界坐标严格满足 `Y > -1`。

## 最快调试方法

1. 打开 `Assets/Scenes/New Scene.unity` 并进入 Play 模式。
2. Game 窗口左上角会显示 Boss 调试面板。
3. 点击“攻击 1/2/3/4/6”可跳过冷却和阶段条件直接验证对应招式。
4. 点击“Boss -10 HP”连续测试 70%、30%、20%、10% 阈值。
5. 用玩家攻击左右护卫，观察其中一侧变暗、碰撞体关闭，以及格式1方向减少。
6. Console 会明确报告玩家、五个挂点或 Animator 参数缺失，不会静默失败。

## Animator

`Visual` 已绑定 `Assets/Sprites/Assets/Boss/Animations/BossBloodLord.controller`。目前没有专属攻击帧，所以 `BossBodyPlaceholder.anim` 对所有逻辑保留 Boss 本体立绘。

控制器参数已经与代码统一：

- `Phase`：Int
- `PhaseChange`、`Death`：Trigger
- `Format1`、`Format2`、`Format3`、`Format4`、`Format5`、`Format6`：Trigger

以后拿到正式攻击动画时，可以在这个 Controller 里用相同 Trigger 增加状态和过渡，不需要修改 Boss 代码。

## 与玩家代码的边界

Boss 伤害只依赖 `IDamageable`、`IKnockbackReceiver`、`IForceKillable`。兼容旧玩家方法的反射适配器位于 Boss 目录；这次没有修改 Player、Enemy 或其他系统脚本。
