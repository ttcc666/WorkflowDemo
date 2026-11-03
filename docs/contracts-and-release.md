# 契约与发布验证

Directory.Build.props 的 ApprovalVersion 与 @approval/ui 的版本一致。四个 NuGet 包分别为 Approval.Core、Approval.AspNetCore、Approval.Forms、Approval.Forms.AspNetCore。Core 和 AspNetCore 不依赖 Forms。

在根目录运行 pwsh -NoProfile -File ./eng/verify-packages.ps1。脚本使用 PowerShell 原生枚举复制消费者，不依赖 ripgrep；剔除 bin/obj/node_modules 等产物。NuGet SourceMapping 将 Approval.* 固定到本次 feed，独立缓存中的包 SHA512 与构建的 nupkg 一致。

Certificate.Api、ReferenceHost 从复制后的宿主源码构建并引用 NuGet 包。EmbeddedHost 只装 Core/AspNetCore，使用已有 Identity、宿主 SaveChanges 和业务表，实际生成 Migration 并运行无 Forms 的审批能力验证。消费者不引用仓库框架源码。

npm tgz 在独立消费者安装，验证 dist 导出、版本、types、CSS 导入并构建真实 demo。可选 RunBrowser 运行消费者页面 smoke。产物记录在 output/packages/<version>/release-manifest.json，包括源码、包、契约与 changelog 摘要。

运行 pnpm contracts:update 更新 OpenAPI、JSON schema、四个公共程序集 API 基线与 TypeScript；CI 用 contracts:check 拒绝漂移。前端 types 依赖生成契约，不能把旧生成内容当成当前协议。

GitHub Actions 必须以实际远端运行结果验收。本地通过只提供本地证据；缺失 remote 或服务环境时在交付报告中保留未满足项。
