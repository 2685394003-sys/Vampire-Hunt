# 怪物燃烧 / 冻结网格特效

当前使用 Piloto Studio 的 `MeshFX_Fire` 和 `MeshFX_Frozen`。只有 Burn（2）和
Frozen（3）走网格特效；Frost（4）仍是霜寒占位表现。玩家暂不接入。

## 配置与接线

- 共享配置：`Assets/VampireHunt/Presentation/StatusMeshVfxConfig.asset`。
- 已接线：`VH_Melee`、`VH_Ranged`、`VH_MeleeEnemy`、`VH_BossHand`。
- `StatusEffectVfxDriver.meshTargets` 留空时使用所有蒙皮网格；没有蒙皮网格则使用普通网格。
- 分体模型只生成一组粒子，其余网格仅增加表面材质。`particleTarget` 可指定发射网格；
  Boss 已指定躯干 `016_衣`，避免粒子只从翅膀发射。
- 粒子从目标网格采样，因此骑士与 Boss 模型已开启 Read/Write。

## 生命周期与约束

状态通过现有 `CombatStatusHost → CombatVfxPresenter → ICombatVfxDriver` 链路投递。
伤害、冻结控制和持续时间不依赖 VFX。重复状态更新复用实例，移除状态或清理 Presenter
会先禁用特效并卸下材质，再销毁对象。

`OverlayFX` 每个实例只拥有并移除自己的材质，不改共享材质资产，也不扫描删除其他
效果的隐藏材质。原预制体的粒子列表不完整，驱动会收集全部子粒子后再绑定和激活。

目前怪物的每个目标 Renderer 都是单子网格；插件的追加材质方式只会覆盖最后一个
子网格。后续接入多子网格模型时，需要先扩展表面绘制方式，不能直接套用此接线。

## 验证

`StatusMeshVfxTests` 覆盖共存、叠层复用、独立移除、跨怪物材质隔离、重新绑定、
Boss 分体覆盖、静态网格、粒子形状与采样、共享配置及玩家不接入。
EditMode 6/6 通过；另做了 Unity 离屏材质与粒子渲染检查。
尚未进行 Host / 远端客户端 / 晚加入联机验收。
