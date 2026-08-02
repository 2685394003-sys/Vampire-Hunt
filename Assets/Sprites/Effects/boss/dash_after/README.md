# B5 冲刺斩残影 (F6)

- 帧数: 12  @ 24 fps
- 尺寸: 1024x1024, RGBA 透明
- 主题: 红色血族 / 顶视角
- 说明: F6 冲刺后的刀光残影条带。

## 文件
- frames/  : PNG 序列帧 (dash_after_0001.png ...)
- contact_sheet.png : 预览拼图
- dash_after.jsx : After Effects 复现脚本

## 在 AE 中使用
1. 打开 After Effects
2. 文件 > 脚本 > 运行脚本文件... > 选择 dash_after.jsx
3. 脚本自动建 1024x1024 合成并导入序列帧
4. 如需导出序列: 渲染队列 > 输出模块选 PNG 序列 + RGB+Alpha
