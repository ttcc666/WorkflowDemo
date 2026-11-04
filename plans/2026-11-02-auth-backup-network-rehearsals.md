# 认证、原生备份恢复与 TCP 故障演练

日期：2026-11-02（Asia/Shanghai）。用户要求继续原方案未运行的三类演练；用户确认没有现成生产认证中心配置。执行本机隔离的真实服务验收，不将其冒称为既有生产环境通过。

状态：本机三库完整演练已完成；最终 runId 为 `ea79988feac24c25817e175aff963307`，SDK 10.0.401，所有制品摘要与清理结果已核对。

## 范围与不变量

- 部署独立 Keycloak HTTPS realm/client/user，走实际 token endpoint、发现文档和 JWKS，验证生产 JWT 管线的签名/issuer/audience/expiry 与企业成员边界。自签证书只在演练进程中按精确指纹固定，不写系统证书库、不修改生产认证默认值。
- 对三个专用 `approval_test_<runId>` 数据库用 mysqldump/mysql、SQL Server BACKUP/RESTORE、pg_dump/pg_restore 做完整原生备份并还原到新名字。审批、操作、防重、在途占用、维护状态、附件内容和 SHA256 都核对；恢复后继续审批、恢复维护和重放原键。
- 用 Toxiproxy 实际拒绝连接和丢弃 commit 下行响应。EF 拦截器只在明确命令的 commit 前安装真实 TCP toxic，不伪造数据库异常。用直连独立上下文核对是否提交，网络恢复后原键重放，不换键重做。
- 演练工具与脚本不可打包；所有数据、realm、网络、容器及私钥属于随机 runId。删除资源前检查所有权；连接只允许明确创建的回环专用测试库。保留非敏感报告和备份，清理私钥/口令文件及 owned Docker 资源。
- 不改动业务数据库、不重置工作区、不提交/部署、不在普通快速回归中静默跳过重型演练。独立入口显式执行且 JSON 列明每个检查和失败阶段。

## 实施清单

- [x] 创建 `eng/Approval.RehearsalTool`：实际参考宿主的生产认证配置、HTTP 操作、状态快照、恢复检查及 TCP 故障控制。
- [x] 创建 `eng/verify-rehearsals.ps1`：固定镜像、HTTPS Keycloak、三库和 Toxiproxy、原生备份/还原、附件副本、所有权清理。
- [x] 验证 JWT 正常审批及错误 audience/issuer/signature、过期和非成员拒绝。
- [x] 三库分别备份/新库还原，核对持久数据与附件摘要并继续未完成审批/维护。
- [x] 三库分别实际 TCP 中断、commit ACK 丢失、原键恢复及单一结果核验。
- [x] 运行普通相关回归/构建，解析演练结果及摘要，更新支持矩阵与未运行的真实生产边界。

## 验证与回退

入口为 `pwsh -NoProfile -File './eng/verify-rehearsals.ps1'`，全部范围顺序执行，任何缺失场景/失败/零检查不得写成通过。报告关联镜像 digest、SDK、Provider/Server、命令阶段、原生备份、数据库快照、附件及 SHA256，不包含令牌/口令/私钥。

运行结束或失败只清理本次 owned 容器/网络和本次私密文件；备份/日志留在 `output/rehearsals/<runId>/`。如失败，先保留非敏感诊断，再修复和重跑，不删除防重行或换提交键掩盖问题。实际生产认证中心、生产备份权限/加密密钥、生产 RPO/RTO、外部业务对账和生产网络拓扑仍须用户提供环境后单独验收。

最终结果：每库 seed 26、auth 11、restore 25 检查通过；network 为 MySQL 12、SQL Server 27、PostgreSQL 12。SQL Server 的 15 个额外检查来自真实连接恢复阶段的基础设施错误重试，所有重试保留原键。合计 237 项演练检查，不作为 xUnit 用例数量。普通后端回归 505 passed / 0 failed / 0 skipped，构建及模型检查通过，公共契约匹配。详见[演练记录](../docs/live-service-rehearsals.md)。
