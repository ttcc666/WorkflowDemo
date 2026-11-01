# 契约与发布验收

## 契约来源

- `eng/Approval.ContractTool` 映射真实审批/注册证路由，在临时回环端口导出 OpenAPI，不初始化业务数据库。
- `contracts/approval.openapi.json` 描述 HTTP；`flow.schema.json`、`form.schema.json` 描述嵌入 JSON 的结构。业务规则仍由服务端校验器执行。
- `contracts/*.api.txt` 记录公开类型、成员、可空性、默认参数和常量；`Certificate.Api.forwards.txt` 记录旧程序集的类型转发。
- `web/packages/approval-ui/src/contracts/http.generated.ts` 由 OpenAPI 生成；`api.ts` 保留原前端名称和投影，并区分服务端可接受的数字字符串与实际写出的数字。
- HTTP 响应 DTO 位于 `Approval.AspNetCore/Contracts`，固定 JSON 样例验证其字段、字符串枚举、UTC 日期和空值与 v1 协议相同。C# 查询服务的既有返回类型保持兼容。

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pnpm --dir 'web' contracts:check
# 有意更改契约后，重新生成并审查差异。
pnpm --dir 'web' contracts:update
```

## 流程协议版本

`schemaVersion` 与流程发布版本、EF 迁移版本相互独立。省略该字段的旧定义按 v1 读取，新保存定义写出 `schemaVersion: 1`。只接受 JSON 整数值（1 与 1.0 等价），未知版本、字符串和 null 均拒绝；前后端复用 `tests/shared/definition-cases.json`。

读取旧定义不会改写数据库中的历史 JSON、DefinitionId 或发布版本；正常编辑后由用户显式发布新版本。将来引入 v2 时，需要独立增加读取升级器、迁移用例和再发布策略，不能通过修改当前版本常量静默升级历史实例。

## 独立包验收

```powershell
pwsh -File './eng/verify.ps1'                         # 本地与 CI 的完整入口
pwsh -File './eng/verify.ps1' -Scope 'Contracts'      # 生成契约一致性
pwsh -File './eng/verify.ps1' -Scope 'Packages'       # 打包、独立消费者与浏览器
```

包版本由 `Directory.Build.props` 的 `ApprovalVersion` 与前端包版本共同校验。构建产物位于 `output/packages/<version>/`，包含三个 NuGet 包、npm tarball 和 `release-manifest.json`。

消费者使用复制的示例宿主代码，替换为 PackageReference / tarball 依赖，不引用库项目或 workspace 源码。独立 NuGet 缓存和 PackageSourceMapping 保证 Approval 包来自本次本地 feed；安装后的 SHA512 与原包逐一比较。前端检查 dist 导出、独立协议类型和 CSS，并执行真实构建。

浏览器在临时端口、新 SQLite 文件上运行，实际操作打包后的设计器、提交与三级审批，检查终审前台账为空、终审后正式生效。Windows 使用已安装的 Edge；其他环境可预先运行 `pnpm --dir 'web' exec playwright install chromium`，也可设置 `APPROVAL_BROWSER_CHANNEL`。测试进程只清理自己启动的服务。

`release-manifest.json` 关联版本、Git 基线、源文件指纹、契约、CHANGELOG 与包校验和；浏览器的 `passed.json` 进一步关联已验收包。CI 保存清单、测试报告和截图。实际向 NuGet/npm 发布或更新运行中的宿主不由此验收脚本执行。
