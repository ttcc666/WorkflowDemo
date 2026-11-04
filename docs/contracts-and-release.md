# 契约与发布验证

Directory.Build.props 的 ApprovalVersion 与 @approval/ui 的版本一致。四个 NuGet 包分别为 Approval.Core、Approval.AspNetCore、Approval.Forms、Approval.Forms.AspNetCore。Core 和 AspNetCore 不依赖 Forms。

在根目录运行 pwsh -NoProfile -File ./eng/verify-packages.ps1。脚本使用 PowerShell 原生枚举复制消费者，不依赖 ripgrep；剔除 bin/obj/node_modules 等产物。NuGet SourceMapping 将 Approval.* 固定到本次 feed，独立缓存中的包 SHA512 与构建的 nupkg 一致。

Certificate.Api、ReferenceHost 从复制后的宿主源码构建并引用 NuGet 包。EmbeddedHost 只装 Core/AspNetCore，使用已有 Identity、宿主 SaveChanges 和业务表，实际生成 Migration 并运行无 Forms 的审批能力验证。消费者不引用仓库框架源码。

npm tgz 在独立消费者安装，验证 dist 导出、版本、types、CSS 导入并构建真实 demo。可选 RunBrowser 运行消费者页面 smoke。产物记录在 output/packages/<version>/release-manifest.json，包括源码、包、契约与 changelog 摘要。

运行 pnpm contracts:update 更新 OpenAPI、JSON schema、四个公共程序集 API 基线与 TypeScript；CI 用 contracts:check 拒绝漂移。前端 types 依赖生成契约，不能把旧生成内容当成当前协议。

远端 GitHub Actions 的结果须来自实际运行；本地通过只提供本地证据，不能代替远端结果。

项目尚未发布，samples 只是一起演进的示例，没有其他已部署宿主。公共 API/OpenAPI 基线变更须逐项审核并验证实际行为；删除旧 API/消息/字段时同步消费者，不保留兼容桥。当前已删除旧公开构造器、墓碑与 Outbox Kind，外部流程 JSON 的 schemaVersion 必填。前端任务类型直接取完整生成响应，DirectoryUser.isActive 必填。具体删除和保留依据见[兼容清理报告](unreleased-compatibility-cleanup.md)。

Core-only 消费者也逐包校验 deps.json 中的 sha512 与本次 feed 字节一致，并实际尝试直接修改 Task/Instance/Operation、删除日志和 bulk 操作；随后继续完成正常审批，证明保护没有阻止业务保存或引擎执行。
