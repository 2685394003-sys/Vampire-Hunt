# C3 阶段切换爆发

- 帧数: 16  @ 24 fps
- 尺寸: 1024x1024, RGBA 透明
- 主题: 红色血族 / 顶视角
- 说明: Boss 进入新阶段的能量爆发(环+放射尖刺+核心闪光)。

## 文件
- frames/  : PNG 序列帧 (phase_0001.png ...)
- contact_sheet.png : 预览拼图
- phase.jsx : After Effects 复现脚本

## 在 AE 中使用
1. 打开 After Effects
2. 文件 > 脚本 > 运行脚本文件... > 选择 phase.jsx
3. 脚本自动建 1024x1024 合成并导入序列帧
4. 如需导出序列: 渲染队列 > 输出模块选 PNG 序列 + RGB+Alpha
