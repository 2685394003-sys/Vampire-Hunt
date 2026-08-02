# C4 死亡血爆溶解

- 帧数: 20  @ 24 fps
- 尺寸: 1024x1024, RGBA 透明
- 主题: 红色血族 / 顶视角
- 说明: Boss 死亡：核心闪光->血色爆炸环->溶解粒子消散。

## 文件
- frames/  : PNG 序列帧 (death_0001.png ...)
- contact_sheet.png : 预览拼图
- death.jsx : After Effects 复现脚本

## 在 AE 中使用
1. 打开 After Effects
2. 文件 > 脚本 > 运行脚本文件... > 选择 death.jsx
3. 脚本自动建 1024x1024 合成并导入序列帧
4. 如需导出序列: 渲染队列 > 输出模块选 PNG 序列 + RGB+Alpha
