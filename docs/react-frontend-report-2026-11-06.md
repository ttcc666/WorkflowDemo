# React 第二套前端实施与验收

日期：2026-11-06。参考分支 `main`，HEAD `27588b1b00d36e9c91e8735094698625f30fcacc`。本次为该 HEAD 上未提交的工作区修改；没有新的提交 SHA 或 CI 结果。**完整门禁及源码 / 打包浏览器验收已通过，退出码均为 0。**

## 实施结果

- 新增 `@approval/shared`、`@approval/react-ui` 和 React Demo；Vue 原入口继续工作，纯业务模块通过重导出 / 适配复用共享实现。
- 完整审批工作台、发起 / 草稿、详情动作 / 权限 / 原键恢复；8 类节点、分支 / 并行 / Join、真实拖拽 / 连线 / 插入 / 撤销重做 / 布局；16 类表单、6 类明细列、公式、人员、附件与校验。
- 全部登记 Action 配置、CodeMirror、Mapping、节点预演及步骤定位、发布治理 / 历史比较、监控 / 交接、消息 / Outbox / Payload / 诊断、证书业务和模拟 Webhook。
- React 19 + Tailwind v4 + Radix；包内 CSS 不使用 Preflight，主题 / utilities 前缀为 `approval`。9 路由 lazy 加载，React Demo 输出独立 dist。
- 边界 / 类型 / 构建 / 包消费者 / CI 工具纳入 React；测试宿主仅使用本次拥有的新 SQLite 与文件目录。

生产后端 `src/`、数据库模型 / Migration、HTTP / JSON 基线、审批语义均未改变。HTTP 类型生成目标移到 shared，Vue 通过公开 contracts 重导出。新增 .NET 测试宿主位于 `eng/consumers/ReactBrowserHost`，不属于生产程序集。

## 验证命令与结果

在仓库根目录：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -SkipRestore
```

仅在依赖已按锁文件安装时使用 `-SkipRestore`。完整命令覆盖 Backend / Frontend / Contracts / NuGet 与 npm 独立包 / Vue 与 packed React 浏览器。单独源码 React：

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow\web'
pnpm 'test:e2e:react'
```

| 验证 | 结果 / 证据 |
| --- | --- |
| Backend 专项 | 本地已通过：Core 694、HTTP 148、SQLite 87、Certificate 59，共 988；Release 0 警告 / 0 错误 |
| Model Drift | 本地 9 Context 检查通过；不是服务数据库执行 |
| ReferenceHost | 本地 27 项通过 |
| Frontend | 本轮全过：Vue UI 256、Vue Demo 6、shared 5、React UI 49、React Demo 1，共 317；189 源文件边界检查、15 边界 / 声明测试、源码 / 测试类型检查和四个 UI / Demo 构建通过 |
| Contracts | 本地专项通过，基线一致；未执行 contracts:update |
| 原独立包 | NuGet 消费者、Vue JS / d.ts / production 消费者、EmbeddedHost 19、TestingHost 22 均通过；本轮 consumer-00a7f1174a21475a92b98da3b80a867f |
| Vue 浏览器 | 12/12，通过；React 用例已从原配置分离 |
| React 独立包 | 两份实际 tgz 安装、typecheck、build、版本一致、无 Vue 依赖、CSS / d.ts 和 native Node ESM 均通过；react-consumer-b9404805-2809-4e5f-a730-afeaf9297ebf |
| React 源码浏览器 | 17/17，0 skipped / unexpected / flaky，当前全部功能修复后执行；react-1793977489003（1.7 分钟） |
| React packed 浏览器 | 17/17，0 skipped / unexpected / flaky，消费上面真实 tgz；react-1793977636171（49.9 秒） |
| MySQL / SQL Server / PostgreSQL | 本地未运行；按仓库约定在 CI 对推送 SHA 验证 |

最终完整日志：`output/engineering/react-final-verify.log`。包验证：`output/engineering/react-consumer-*/verification.json`（包含包 SHA-256）。浏览器：`output/playwright/react-*/results.json`、`context.json`、日志与失败 trace。生成文件不纳入源码。

本轮精确证据：

- [完整门禁日志](../output/engineering/react-final-verify.log)：末尾 `Verification succeeded: All`。
- [最终源码浏览器 JSON](../output/playwright/react-1793977489003/results.json)：17 个期望场景，errors 为空。
- [最终 packed 浏览器 JSON](../output/playwright/react-1793977636171/results.json)：17 个期望场景，errors 为空。
- [Vue 浏览器 JSON](../output/engineering/consumer-00a7f1174a21475a92b98da3b80a867f/browser/results.json)：12 个期望场景。
- [独立 React 包证据及哈希](../output/engineering/react-consumer-b9404805-2809-4e5f-a730-afeaf9297ebf/verification.json)。

本轮完整门禁起先在新增测试的类型检查处失败；修正 fixture 与 Testing Library 参数后重新运行。首次失败日志另存 `output/engineering/react-verify-typecheck-failure.log`，结果以最终成功退出的运行及当前浏览器 JSON 为准。

实际打包浏览器进一步发现 CommonJS require(react) 的运行错误，失败日志另存 `output/engineering/react-verify-packed-failure.log`。已将声明依赖通过公开 imports 交给宿主构建；最终包必须通过真实浏览器，不能用 consumer build 代替此检查。

未修改的后端 P4Safety 并发用例曾在四项目并行执行时耗尽 30 秒取消保护，日志另存 `output/engineering/react-verify-backend-timeout.log`。原样单独执行三种撤权分区，3/3 通过；验证脚本使用 `-m:1` 顺序执行测试项目，原测试内部并发、屏障、取消保护和断言不变，本轮 988 个后端测试全部通过。

## 核心可观察断言

| Requirement | Evidence |
| --- | --- |
| 审批原键原正文恢复与加载隔离 | `React saves and restores a draft submits a form and recovers an approval after the committed response is lost`：两次键 / 正文一致，amount=250，恰好一条 Approve；`React waits for the editable form definition before allowing approval confirmation`：显式阻挡定义响应，动作禁用，放行后一次审批成功 |
| 退回 / 驳回 / 加签 / 转交 / 管理动作 | `React returns an approval restores the original values and resubmits the edited form`；`React rejects with a required reason and records one terminal rejection without altering form values`；`React After add-sign keeps the required approval participants`；`React Parallel add-sign keeps the required approval participants`；其余精确映射见功能表 K02 |
| Vue / React 双向配置兼容 | `React and Vue preserve all field node and Action configuration on both editing directions`：全部节点、字段、配置版本、嵌套 JSON、默认分支、权限、通知与设置保持 |
| 真实画布编辑 | `React canvas connects approval nodes saves a real draft and Vue can edit the same definition`；`React palette drop inserts on a conditional edge and undo redo preserves its default branch and ordering` |
| 所有 Action 实际执行 / 自动重试 | `React publishes every Action and shared backend retries HTTP 503 with one stable execution key`：发布 v2，SQL 库存 30、同步 GET 42、公式 427、503→200 原键原正文、最终 Approved、普通日志脱敏 |
| 表单类型 / 明细 / 精度 / 人员 / 校验 | `renders all sixteen schema field types without changing input values`；`adds and deletes Table rows with six supported column types and computes row formulas and totals`；`validates nested detail errors switch required precision date order and user limits`；`passes readonly and editable user states to the host picker without changing ID types` |
| 权限 / 无部分写入 / 旧身份隔离 | `missing keys forbidden editable fields stale revisions and identity changes cannot produce partial writes`；`disables an open approval dialog when its resource permission is revoked` |
| 无副作用预演 | `React previews a JavaScript action with synthetic data and leaves draft and external systems untouched`：固定 amount=500、score=2；原 revision / draft / 远程交换不变 |
| 宿主集成 / 异步状态 | `keeps two owned Providers independent through StrictMode and identity changes with Forms disabled`；`restores prefixed URL pagination and filters on load and history navigation and refreshes an unchanged search` |

## 取舍、限制与复核

[功能表](../web/packages/approval-react-ui/FEATURE_PARITY.md) 保留 178 个编号，映射实际实现和精确用例。它是实现及组合回归清单，不能理解为逐项 178 个独立自动化浏览器场景。长时间 CodeMirror 编辑、全部移动设备、每个授权组合未逐一穷举；未测量覆盖率或运行 mutation tool。

当前 Schema 不提供正则 / 通用联动、任意 SQL / HTTP URL、独立 Action 步骤执行或新响应映射。保留当前协议能力；Action ruleIndex 是审批人规则索引。测试 HostEcho 明确登记 configVersion=3。

SQLite Worker / HTTP 竞争中曾出现 30 秒锁等待；自动重试后恢复。浏览器给真实异步 Worker 65 秒轮询窗口，仍检查全部执行结果，不改后端或掩盖失败。CodeMirror lazy chunk 约 503 KB（minified）有 Vite 大 chunk 提示；没有通过抬高告警阈值消除提示。

已使用 code-testing / assertion-quality / test-gap-analysis 做断言与业务变化映射审查，发现和修复见 [测试质量审查](react-test-quality-review.md)。未删除或 skip 原 Vue / 后端用例；未提交、推送、合并、发布或操作用户数据库。
