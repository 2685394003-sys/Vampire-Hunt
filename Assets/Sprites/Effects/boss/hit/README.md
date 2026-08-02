# C2 受击血溅

- 帧数: 12  @ 24 fps
- 尺寸: 1024x1024, RGBA 透明
- 主题: 红色血族 / 顶视角
- 说明: Boss 受击时的血溅粒子爆发。

## 文件
- frames/  : PNG 序列帧 (hit_0001.png ...)
- contact_sheet.png : 预览拼图
- hit.jsx : After Effects 复现脚本

## 在 AE 中使用
1. 打开 After Effects
2. 文件 > 脚本 > 运行脚本文件... > 选择 hit.jsx
3. 脚本自动建 1024x1024 合成并导入序列帧
4. 如需导出序列: 渲染队列 > 输出模块选 PNG 序列 + RGB+Alpha
