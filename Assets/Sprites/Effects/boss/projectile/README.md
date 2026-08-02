# B2 弹幕弹丸+拖尾 (F2)

- 帧数: 12  @ 24 fps
- 尺寸: 1024x1024, RGBA 透明
- 主题: 红色血族 / 顶视角
- 说明: F4/F2 弹幕弹丸，箭头朝下(由代码旋转朝向速度)，含能量拖尾与自旋高光。

## 文件
- frames/  : PNG 序列帧 (projectile_0001.png ...)
- contact_sheet.png : 预览拼图
- projectile.jsx : After Effects 复现脚本

## 在 AE 中使用
1. 打开 After Effects
2. 文件 > 脚本 > 运行脚本文件... > 选择 projectile.jsx
3. 脚本自动建 1024x1024 合成并导入序列帧
4. 如需导出序列: 渲染队列 > 输出模块选 PNG 序列 + RGB+Alpha
