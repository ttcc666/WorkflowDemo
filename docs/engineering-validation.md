# 工程化改造验证记录

日期：2026-11-01。改造范围与不变量见[实施方案](../plans/2026-11-01-engineering-and-extensibility.md)。此文件随阶段验收更新，未完成项不代表通过。

当前边界已校正：注册证位于 `samples/Approval.Certificate`，不作为框架包发布。下面的阶段 0–5 保留首次改造的验收历史；当时的三个 NuGet 包已收敛为两个框架包，后续验收方式见“注册证示例归属校正”。

## 原始基线

- Workflow Release 构建：0 warnings、0 errors。
- Approval.Core.Tests：257 passed，0 failed，0 skipped。
- Approval.AspNetCore.Tests：43 passed，0 failed，0 skipped。
- Certificate.Api.Tests：59 passed，0 failed，0 skipped。
- EF `has-pending-model-changes`：没有模型差异。
- 前端：组件库 136 tests、示例 6 tests 通过；typecheck、build:lib、build 均通过。
- Admin 默认输出目录被正在运行的宿主占用（MSB3021/MSB3027），改用 `--artifacts-path` 隔离编译产物；未停止既有宿主。隔离基线为 58 passed、0 failed、0 skipped。

## 阶段验收

| 阶段 | 状态 | 证据 |
|---|---|---|
| 0 公共配置与统一验证入口 | 已完成 | `eng/verify.ps1` 完整执行成功；14 个直接依赖版本保持不变；原有 359 项后端、142 项前端测试通过，迁移无差异 |
| 1 注册证模块与 Admin 联动 | 已完成 | 新模块构建 0 warnings / 0 errors；原有 359 项后端测试通过，新增 5 项模块兼容测试通过，Admin 58 项通过；迁移无差异；已移除 Admin 的示例源码链接 |
| 2 引擎职责 | 已完成本轮范围 | scoped 扩展目录、独立人员解析/资格服务、公共构造函数兼容；Workflow 258 Core + 43 HTTP + 64 Certificate 测试通过，迁移无差异；Admin 58 项通过 |
| 3 前端契约与模型 | 已完成 | contracts 独立类型入口；图/表单模型拆分；41 个文件类型引用迁移；类型检查、原有 142 项前端测试、库/示例构建通过；边界检查已接入 CI |
| 4 实例客户端 | 已完成本轮范围 | 独立客户端、注入、用户/目录缓存、路由批次隔离；前端总计 151 library + 6 demo 测试通过，另有 7 项依赖边界测试；Admin 前端 15 项测试、typecheck:approval、build 通过 |
| 5 契约与发布门禁 | 已完成 | 首次验收的统一入口退出码 0；378 项后端、166 项前端、7 项边界测试通过；契约检查通过；当时三个 NuGet 包与 npm tarball 的独立消费者通过；两项浏览器验收通过 |

阶段 1 的新增证据为 `ModulePackagingTests.Legacy_assembly_names_forward_to_the_certificate_module`（3 个旧公共类型）、`Module_embeds_all_default_certificate_workflows` 和 `Module_preserves_migration_history_and_model`。三份默认流程已复制到类库构建输出，独立发布包消费将在阶段 5 验收。

阶段 2 的行为修正独立记录：原 Outbox 对同名渠道使用 FirstOrDefault，而设计器元数据使用最后注册项。`ChannelOverrideTests.Last_registered_channel_overrides_earlier_channels_in_metadata_and_retries` 在原实现中失败（预期 Pending，实际 Succeeded），证明投递没有调用元数据选定的渠道。统一为后注册覆盖后的 258 项核心测试通过；随后提取扩展目录与人员服务，全套 365 项后端测试及迁移检查通过。

阶段 3/4 保持旧单实例入口，新的实例隔离入口见[客户端接入](client-instances.md)。新增测试覆盖配置/令牌/面板隔离、401 与字段错误、幂等键、上传/下载/取消、两个 Vue 应用、同 ID 目录缓存、身份切换迟到响应、共享缓存生命周期、两个 router 与同 router 的 query 合并。未新增控制流节点、表单字段类型或同 router 路由命名空间，计划中的对应项仍是条件项。

阶段 5 最终统一验收已完成，日志为 `output/engineering/final-verification.log`，结束为 `Verification succeeded: All`。后端为 Core 268、HTTP 46、Certificate 64；前端为 UI 160、demo 6，另有 7 项边界测试。Admin 最终后端 58 项、前端 15 项、类型检查及构建均通过。

## 最终要求与证据

| Requirement | Evidence |
|---|---|
| JSON 字段、枚举、UTC 日期、null 保持兼容；实体变化不影响响应对象 | `HttpResponseContractTests.Instance_response_preserves_v1_json_and_detaches_entities`、`Definition_message_and_outbox_responses_preserve_v1_json`，固定 v1 JSON 样例 |
| 旧流程按 v1 读取，当前版本显式写出，未来/非法版本拒绝 | `DefinitionContractTests.Shared_definition_contract_accepts_legacy_and_current_versions_only`、`Direct_future_definition_cannot_be_validated_or_serialized_as_current`；前端 `shared definition contract` 共用同一 JSON 用例 |
| 接口、Schema、公开成员和 TS 类型变化可见 | `pnpm contracts:check` 通过；有意修改 `form.schema.json` 的 $id 后检查失败，恢复原字节后通过 |
| 版本与产物对应，消费者使用本次包字节 | `output/packages/0.2.0/release-manifest.json`：版本、Git 基线、源码指纹、契约/CHANGELOG/包 SHA256；独立 NuGet 缓存中安装包的 SHA512 与源 nupkg 相同 |
| 前后端可以脱离库源码构建 | 消费者 `consumer-354f770758db4d2ca5228ed3eaf67bd9`：零库 ProjectReference，NuGet 包类型依赖、npm dist 导出、独立协议类型、CSS、三份默认流程与迁移资源；两端构建通过 |
| 打包后的真实 UI 完整审批链路 | `packaged designer publishes a workflow and three approvals activate the certificate`：修改节点名称/发布版本、提交、终审前不入账、三级审批后生效、截图和浏览器版本附件 |
| 未授权或不支持的协议不造成写入 | `packaged endpoints reject anonymous access and unsupported protocol versions`：401、未知 v2 返回 400、拒绝的定义仍为 404 |

浏览器结果位于消费者 `browser/results.json` 和 `browser/passed.json`，报告为 expected=2、unexpected=0、skipped=0、flaky=0，校验和与发布清单一致。Windows 验收使用已安装的 Edge；Chromium 下载遇到网络超时后已停止该下载进程。检查用临时服务及最终验收服务均已清理，现有 Admin 服务未被停止。

验收使用包含全部改造的工作区，原集成适配与本次工程化改造分别归入本地提交。未执行包发布或更新运行中的业务服务。以后新增控制流语义、自定义字段和同 router 命名空间时，按计划中保留的条件项另行设计。

## 注册证示例归属校正（2026-11-02）

- `src/` 仅保留 `Approval.Core` 与 `Approval.AspNetCore`；注册证示例类库设置 `IsPackable=false`，与示例宿主一起位于 `samples/`。28 个 C# / 流程 JSON 文件与移动前的 Git 内容一致，程序集名称、命名空间和迁移 ID 保持兼容。
- 框架 OpenAPI 保留 47 条路径，5 条注册证路径移入 `samples/Approval.Certificate/contracts/certificate.openapi.json`；逐项比较确认请求、响应与已有 Schema 不变。示例的公开 API / 类型转发基线也移入该目录，UI 包不再生成注册证类型。
- 发布清单只列出两个框架 NuGet 包及 UI tarball。独立消费者复制注册证示例源码，通过 PackageReference 使用框架；验收同时断言框架依赖来自本次包、注册证依赖来自复制的示例项目。
- Admin 集成的项目引用与存在性检查同步指向新路径；使用独立编译目录验证，保留已有服务及该仓库的其他改动。

本次后端构建为 0 warnings / 0 errors，Workflow 为 378 passed / 0 failed / 0 skipped，EF 模型无差异，Admin 后端为 58 passed / 0 failed / 0 skipped。前端 166 项测试与 7 项边界测试、类型检查、库/示例构建以及契约生成一致性检查均通过。Admin 的 `typecheck:approval` 通过。独立消费者构建通过，浏览器最终结果为 2 passed / 0 failed / 0 skipped / 0 flaky。

浏览器首轮在 `packaged designer publishes a workflow and three approvals activate the certificate` 的最终展示断言中失败：按证号片段查找整行，在路由切换期间同时匹配了审批详情和证书列表。后台三级审批和正式生效断言已通过，失败现场也显示证书有效。现改为等待证书列表路由、精确定位证号单元格所在的唯一行，并检查同一行的“有效”单元格。使用同一批包重跑两项浏览器验收后，进程退出码为 0，JSON 报告和包校验和均核验通过。

本次各阶段日志位于 `output/engineering/certificate-sample-*.log`。`certificate-sample-packages.log` 保留独立消费者构建及浏览器首轮失败的记录；`certificate-sample-browser.log` 是修复后的成功复验。初次浏览器现场保存在消费者的 `browser-before-locator-fix/`，最终报告和 `passed.json` 位于 `browser/`。包与浏览器的最新关联关系见 `output/engineering/package-verification.json`。

发布清单的源码指纹对应打包时的工作区；此后只调整了浏览器测试定位及本验收记录，浏览器复验使用的三个包文件与清单中的 SHA256 完全一致。

## 重现

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -File './eng/verify.ps1'
```

实际 Admin 宿主验证使用隔离输出目录，保留当前服务运行：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Admin.Core'
dotnet test 'src/modules/admin/ZhonTai.Admin.Tests/ZhonTai.Admin.Tests.csproj' --configuration 'Release' --verbosity 'minimal' --artifacts-path 'D:/Study/CSharp/Workflow/output/engineering/admin-build'
```
