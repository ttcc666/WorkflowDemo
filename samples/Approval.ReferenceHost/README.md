# 单宿主参考应用

业务无关的嵌入式审批宿主，使用真实 Approval.AspNetCore 路由。身份/用户目录/授权适配器只是宿主示例，不属于引擎独立用户或权限系统；生产替换为既有组织 SDK。

生产默认 JWT，要求 HTTPS authority 和 audience；Development 才允许明确配置的随机演示令牌。Directory:Users 是单宿主用户配置，没有 TenantId、租户 Claim 或目录路由。初审 Reviewer，终审 FinalReviewer；变更权限立即影响后端操作及前端 capabilities。

```powershell
Set-Location -LiteralPath 'D:/Study/CSharp/Workflow'
pwsh -NoProfile -File 'eng/verify-reference-host.ps1'
```

脚本使用独立 SQLite 文件、随机令牌和临时端口，先显式迁移再启动 Kestrel。验证无键请求拒绝、原键重放、当前权限撤销/恢复、审批完成及业务占用释放，产物位于 output/reference-host。只停止本次启动的进程。

宿主 DbContext 继承 ApprovalDbContext(options)，调用 ConfigureApproval(this)；服务由 AddApprovalWorkflow<T>() 注册。SQL Server/MySQL/PostgreSQL 迁移由四套 ReferenceContext 拥有。旧迁移链保留；EmbeddedApproval 的归属/自动化阻断和升级步骤见 [迁移说明](../../docs/embedded-approval-completion.md)。

默认不在启动时自动迁移已有业务库；使用 --migrate 的独立维护窗口，停止旧 Worker、备份并在副本先演练。生命周期维护和删除默认关闭，后台只有一个 ApprovalWorker。
