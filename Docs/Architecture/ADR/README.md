# Architecture Decision Records

本目录存放对 [ARCHITECTURE.md](../../../ARCHITECTURE.md) 规范的例外与重大架构决策记录（见 ARCHITECTURE §17）。

## 格式

文件名：`NNNN-短横线小写标题.md`（NNNN 从 0001 递增）。

每份 ADR 至少包含：

1. **问题**：需要解决的问题。
2. **原因**：无法遵守现有边界的原因。
3. **替代方案**：评估过的其他选项。
4. **影响与测试**：影响范围和测试方案。
5. **时效**：是否临时、清理期限和负责人。

## 索引

| 编号 | 标题 | 状态 |
|---|---|---|
| 0001 | [玩家移动采用 Owner 客户端权威 + 服务器校验](0001-owner-authoritative-movement.md) | Adopted |
