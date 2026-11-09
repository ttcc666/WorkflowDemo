# React / Vue 功能差异修复验收

基于 `main` / `4073933a17d78e15343ed686466470bfc8de1862` 的工作区实施。已核对远端 main 与本地 HEAD 一致。仓库原有的后端、契约、文档和 package.json 未提交改动均保留；本次没有提交或推送。

## 修复范围

- `web/packages/approval-react-ui/src/pages/ApprovalDetailPage.tsx`：宿主 BusinessPanel 优先于 DynamicForm；无面板时保留动态表单及原始数据两级回退。面板按实例 ID 和状态重新挂载，仍只传递现有 businessKey。使用宿主面板时，审批不再被未挂载的 DynamicForm 校验引用阻止。
- `web/packages/approval-react-ui/src/features/workbench/ListFilterBar.tsx`：增加内部 children 插槽及 loading 控制，复用原筛选布局。
- `web/packages/approval-react-ui/src/features/monitor/MonitorPanels.tsx`：通过现有 list.filters.initiatorId 和 UserPicker 组合发起人筛选；自动回第一页，清除和重置同步移除 API / URL 参数。交接增加同用户校验、接收人自动清除、重复提交保护及身份 / 卸载后的结果保护，保留确认、幂等恢复与跳过结果。
- 同一 MonitorPanels 文件补齐统计：11 项摘要指标及完整流程状态列；复用 shared.durationText，null 保留占位符；本地最近 30 天默认范围和排他的次日零点；真实流程 Select；待应用提示、有效数据范围、加载、错误、上次成功结果、请求取消和身份隔离。
- `web/packages/approval-react-ui/src/pages/MonitorPage.tsx`：以 React Router search params 管理 Tab，使用 replace 保留其他参数；非法或未授权 Tab 回退到首个授权项，无权限继续拒绝。

没有新增依赖，没有修改后端业务、HTTP / JSON / TypeScript 公共契约、数据库模型或 Vue 源码。

## 自动化测试

新增 `ApprovalDetailPage.test.tsx`、`MonitorInstances.test.tsx`、`HandoverPanel.test.tsx`、`MonitorPage.test.tsx`，扩展 `MonitorStatistics.test.tsx`。5 个定向文件共 44 项，其中新增 36 项，原有 8 项统计测试保留并按新日期 / Select 行为更新。

扩展 `web/tests/browser/react/operations.spec.ts` 至 1280px / 390px，验证真实交接 POST、幂等键、冲突接收人清除及逐人抄送。`pages.spec.ts` 新增两个宽度的监控组合场景，验证真实 API 参数、日期、流程、发起人清除、刷新恢复和权限。`certificates.spec.ts` 保留三阶段审批及最终生效 / 历史断言，先同步等待各步真实 204 响应，并增加每步调用次数检查。

| Requirement | Evidence |
| --- | --- |
| 面板优先且仍能审批 | `registered business panel takes priority over a loaded dynamic form and still allows approval` |
| 动态表单回退、实例数据与拒绝权限 | `without a business panel the dynamic form displays the instance data and denied actions stay hidden` |
| 原始内容回退 | `without a business panel or form the default content displays raw business data` |
| 实例 / 状态切换更新面板 | `changing instance or status remounts the panel with the current business key` |
| 发起人 API、组合条件、分页和 URL | `initiator selection composes every applied filter resets pagination and preserves unrelated URL parameters` |
| 发起人清除 | `clearing the initiator removes its API and URL parameters while preserving other filters` |
| 列表重置 | `reset clears the initiator and all list filters and restores the first page` |
| 无效组合条件与列表权限 | `invalid draft dates prevent initiator changes from issuing an invalid combined request`; `users without read-all permission never request the administrative instance list` |
| 同用户按钮及提交前拒绝 | `the same handover user disables submission and cannot issue a POST even from an open confirmation` |
| 接收人冲突清除及旧结果重置 | `changing the handing-over user clears a conflicting receiver and resets the result` |
| 真实交接意图、确认、幂等键、跳过结果 | `different handover users submit once with an idempotency key and display skipped task results` |
| 失败后保留窗口并按原键重试 | `failed handover keeps the confirmation and retries with the original key and body` |
| 交接重复提交及权限 | `a pending handover disables repeated confirmation and user edits`; `users without bulk-reassign permission cannot preview or submit handover tasks` |
| 默认本地最近 30 天及一次初始化请求 | `default statistics explicitly query the last 30 local dates including today once` |
| 全部真实状态及可读耗时 | `statistics show all real status metrics and shared readable duration units` |
| null、零值、分钟 / 小时 / 天 | `duration values null and null keep unknown values distinct from zero`; `duration values 0 and 45 keep unknown values distinct from zero`，以及前一行的小时 / 天断言 |
| 闰日、排他结束边界及默认重置 | `valid leap day statistics apply local inclusive bounds and reset both the query and inputs` |
| 流程选择与清除、保留日期 | `flow selection applies its definition key with dates and clearing queries all flows` |
| 非法日期不请求 | `invalid statistics date 2026-02-30 keeps the last result and never queries`（另含不完整日期、非闰年、零年和超出四位年份）；`reversed statistics dates show field feedback without applying a query`; `an empty statistics date is rejected and never silently uses a backend default` |
| 待应用提示、失败保留成功数据与实际范围 | `a failed query keeps the last successful statistics and the actual successful filter range` |
| 重试使用已请求条件 | `statistics retry uses the applied query even when the editable date is incomplete` |
| 取消旧查询及避免重复刷新 | `new flow queries cancel stale requests and pending refresh cannot submit duplicates` |
| 身份切换清理和拒绝旧结果 | `identity changes clear retained statistics and late old responses cannot replace the new result` |
| 流程列表失败 / 空数据 | `flow list failure keeps statistics available and retry reloads only the flow options`; `an empty flow list still offers all flows and keeps the default statistics` |
| 统计权限 | `users without statistics permission never request administrative data` |
| Tab 替换 URL、保留条件、浏览器历史 | `switching monitor tabs replaces the URL and preserves existing filters without adding history` |
| 刷新恢复 | `reloading a monitor URL restores the previously selected tab` |
| 直接打开授权 Tab | `direct URL access to statistics selects only the authorized requested panel`，同组另含 handover / instances |
| 未授权 / 非法 Tab 回退 | `unauthorized or invalid URL tab handover falls back to the first permitted tab and preserves parameters`，同组另含 invalid / instances |
| 全部监控权限缺失 | `no monitor permissions keeps the denial page and sends no administrative requests` |
| 桌面和手机真实交互 | `monitor filters statistics and URL tabs at 1280 use real API parameters and preserve permissions` 及 390px 同组用例；两种宽度的真实 bulk handover 用例 |

## 本地验证结果

| 命令（在 web 下） | 结果 |
| --- | --- |
| `pnpm check:boundaries` | 通过，231 个源文件 |
| `pnpm check:ui:react` | 通过，44 个消费者文件 |
| `pnpm test:ui:react` | 3 项通过 |
| `pnpm test:boundaries` | 15 项通过 |
| `pnpm typecheck` | 5 个 workspace 项目全部通过 |
| `pnpm test -- --maxWorkers=2` | 420 项通过：shared 17、React UI 136、Vue UI 256、Vue Demo 6、React Demo 5 |
| `pnpm build:react-lib` | 通过，包含声明生成 |
| `pnpm build:react` | 通过 |
| `pnpm build:lib` | Vue 库及声明生成通过 |
| `pnpm verify:react-package -- --skip-build` | 最终 tgz 消费者通过：类型、构建、原生 ESM、无 Vue 依赖 |
| `$env:APPROVAL_BROWSER_CHANNEL='chromium'; pnpm test:e2e:packed` | 最终 66 项全部通过，0 skipped / unexpected / flaky；JSON 报告已解析核验 |

最终包验证证据：`output/engineering/react-consumer-65369d35-6065-4ad0-ab81-63c9d2311dc0/verification.json`。最终完整浏览器报告：`output/playwright/react-1794207867453/results.json`（66 项全部通过，5.4 分钟）。首轮：`output/playwright/react-1794205307247/`；定向 5 项：`output/playwright/react-1794206591784/`；第二轮：`output/playwright/react-1794206894323/`；定向 2 项：`output/playwright/react-1794207599307/`。

浏览器首轮失败均为夹具适配：两个宽度产生同名抄送记录，改为按真实业务编号定位对应行；初始化脚本会在导航时覆盖用户切换，改为仅首次初始化并显式等待无权限提示；既有百分比用例改用新流程 Select，仍验证后端实际 100% 与 null 的未知显示。没有删除用例、放宽业务断言或调整 Playwright 超时 / retries。

原注册证用例没有同步审批响应，只通过窗口关闭推断操作完成；错误截图中最终审批与业务均已完成。现在先等待响应后执行原窗口断言，并检查响应与次数。另一项为 Edge browserContext.close 中断，未修改该布局用例，原用例定向重跑通过。最终完整验收通过现有 APPROVAL_BROWSER_CHANNEL 覆盖使用已安装的匹配版 Chromium，仓库默认 Edge 配置保持原值。

首轮 Vitest 冷启动触发其固定 60 秒 worker 启动等待限制。诊断时仅临时延长本地 node_modules 内的启动等待，未改测试断言或用例超时；该 runner 已逐字还原，最终 420 项通过结果由原 runner 取得。

服务数据库矩阵与 GitHub Actions 未运行；本次未推送提交。Release 示例宿主只做了浏览器宿主构建及本地隔离 SQLite 运行，不能替代后端或各 Provider 的完整测试。

本次代码 / 测试涉及 12 个文件，另加本验收文档。全部工作区既有改动均不计入本次 diff 摘要。

P1–P5 均已完成，在上述验证范围内未发现 Vue 功能回归。没有未完成的修复项；Edge 清理阶段的一次浏览器关闭错误未复现，其历史失败记录保留，不能描述为 Edge 全套已经通过。
