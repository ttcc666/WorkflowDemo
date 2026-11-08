# React 全页面优化与验收

2026-11-07，本地未提交工作区，基于 HEAD `27588b1b00d36e9c91e8735094698625f30fcacc`。本报告承接“逐页检查并优化”的工作，补齐被聊天图片数量上限中断的最终复核。

## 修改与取舍

- 基础布局：`web/packages/approval-react-ui/src/components/ui/PageHeader.tsx`、`DataTable.tsx`、`Metrics.tsx`、`primitives.tsx` 与 `src/styles.css` 统一标题、带标签的筛选、关联字段层级、移动卡片、表单间距和弹窗。修复 Tailwind 前缀下圆角 token 缺少别名导致小控件实际呈直角的问题。
- 申请与治理：`src/pages/StartCenterPage.tsx` 优先显示申请模板，支持分类／关键词组合筛选，草稿有独立入口；`DefinitionsPage.tsx` 增加名称／Key 搜索并保留服务端分类筛选；`StartFormPage.tsx` 整理申请说明、字段、保存与提交栏。
- 设计器：`DesignerPage.tsx`、`features/forms/FormDesigner.tsx`、`FlowSettingsPanel.tsx`、`features/designer/ActionStepsEditor.tsx` 整理操作区、字段库、属性、步骤与预演。保留原有工作流／表单 Schema 和业务交互，桌面分区，窄屏依次排列。
- 监控与诊断：`pages/MonitorPage.tsx`、`DiagnosticsPage.tsx` 与 `features/monitor/MonitorPanels.tsx` 整理统计、待办积压、风险发现和待办交接。修复通过率显示重复乘 100：后端 `100` 显示为 `100.0%`，缺失值显示 `—`。
- 投递：`pages/OutboxPage.tsx` 将事项、关联审批、状态、尝试与时间归组；Payload 解开 HTTP 响应内的 JSON 字符串，按层级显示中文，另提供精确原文；非 JSON 内容保留原文。加载状态、窄屏长内容和原敏感读取授权保留。
- 注册证与宿主：`web/apps/react-demo/src/pages/CertificatesPage.tsx`、`features/CertificateFormDialog.tsx`、`CertificateRequestPanel.tsx`、`CertificateDiagnosticsPanel.tsx`、`DemoWebhookPanel.tsx` 整理列表、业务信息、历史、表单及诊断。Demo 的 `main.tsx`、`styles.css` 同步导航和移动布局。

本次全页面优化没有改变公共 C# API、HTTP／JSON／TypeScript 契约、数据库模型、审批业务规则或 Vue 稳定前端。保留真实权限、字段隐藏／编辑、稳定操作键、未知提交结果恢复、只读发布版本、附件访问和离开未保存页保护。

## 逐页复核

自动布局检查覆盖 14 个页面／子面板 × 1280px／390px。人工复核包含主页面、设计器属性、申请草稿、审批／通知及注册证弹窗；以下截图来自同一隔离 SQLite 宿主中的真实 API 数据。

| 页面／面板 | 重点 | 桌面 | 手机 |
| --- | --- | --- | --- |
| 审批工作台 | 真实个人摘要、分类、列表、长标题及处理入口 | [截图](../output/visual-review/all-pages/workbench-desktop.jpg) | [截图](../output/visual-review/all-pages/workbench-mobile.jpg) |
| 审批详情 | 内容、可编辑字段、轨迹、流程／Action／抄送入口 | [截图](../output/visual-review/all-pages/approval-detail-desktop.jpg) | [截图](../output/visual-review/all-pages/approval-detail-mobile.jpg) |
| 发起中心 | 模板优先、搜索／分类、空态 | [截图](../output/visual-review/all-pages/start-desktop.jpg) | [截图](../output/visual-review/all-pages/start-mobile.jpg) |
| 我的草稿 | 独立入口、继续编辑和删除 | [截图](../output/visual-review/all-pages/drafts-desktop.jpg) | [截图](../output/visual-review/all-pages/drafts-mobile.jpg) |
| 动态申请 | 申请说明、字段、保存／提交、草稿恢复 | [截图](../output/visual-review/all-pages/start-form-desktop.jpg) | [截图](../output/visual-review/all-pages/start-form-mobile.jpg) |
| 流程定义 | 名称／Key 与分类、版本／治理和操作 | [截图](../output/visual-review/all-pages/definitions-desktop.jpg) | [截图](../output/visual-review/all-pages/definitions-mobile.jpg) |
| 流程设计 | 工具栏、节点库、画布与属性 | [截图](../output/visual-review/all-pages/designer-flow-desktop.jpg) | [截图](../output/visual-review/all-pages/designer-flow-mobile.jpg) |
| Action 属性 | 步骤展开、编辑、定位、单节点预演 | [截图](../output/visual-review/all-pages/designer-action-desktop.jpg) | [截图](../output/visual-review/all-pages/designer-action-mobile.jpg) |
| 表单设计 | 字段库、17 字段夹具、属性、公式／明细配置 | [截图](../output/visual-review/all-pages/designer-form-desktop.jpg) | [截图](../output/visual-review/all-pages/designer-form-mobile.jpg) |
| 发起设置 | 分类、角色、说明、标题模板变量 | [截图](../output/visual-review/all-pages/designer-settings-desktop.jpg) | [截图](../output/visual-review/all-pages/designer-settings-mobile.jpg) |
| 实例监控 | 查询、实例信息和分页 | [截图](../output/visual-review/all-pages/monitor-desktop.jpg) | [截图](../output/visual-review/all-pages/monitor-mobile.jpg) |
| 统计 | 服务端数值、通过率、节点与待办表格 | [截图](../output/visual-review/all-pages/statistics-desktop.jpg) | [截图](../output/visual-review/all-pages/statistics-mobile.jpg) |
| 离职交接 | 两个人员选择、预览、原因与禁用状态 | [截图](../output/visual-review/all-pages/handover-desktop.jpg) | [截图](../output/visual-review/all-pages/handover-mobile.jpg) |
| Outbox | 长 ID、关联审批、尝试／状态和原文 | [截图](../output/visual-review/all-pages/outbox-desktop.jpg) | [截图](../output/visual-review/all-pages/outbox-mobile.jpg) |
| 运行诊断 | 就绪／风险、真实计数、发现与详情 | [截图](../output/visual-review/all-pages/diagnostics-desktop.jpg) | [截图](../output/visual-review/all-pages/diagnostics-mobile.jpg) |
| 注册证 | 列表、状态、历史、登记／变更／注销和宿主诊断 | [截图](../output/visual-review/all-pages/certificates-desktop.jpg) | [截图](../output/visual-review/all-pages/certificates-mobile.jpg) |

附加复核：[审批弹窗](../output/visual-review/all-pages/approval-dialog-mobile.jpg)、[通知](../output/visual-review/all-pages/notifications-mobile.jpg)、[登记](../output/visual-review/all-pages/certificate-create-mobile.jpg)、[变更](../output/visual-review/all-pages/certificate-change-mobile.jpg)、[注销](../output/visual-review/all-pages/certificate-revoke-mobile.jpg)、[证书历史](../output/visual-review/all-pages/certificate-history-mobile.jpg)、[Payload 桌面](../output/visual-review/all-pages/payload-desktop.jpg)／[手机](../output/visual-review/all-pages/payload-mobile.jpg)、[模拟输入](../output/visual-review/all-pages/designer-simulation-mobile.jpg)和[模拟校验失败反馈](../output/visual-review/all-pages/designer-simulation-result-mobile.jpg)。模拟成功路径由现有 `simulation.spec.ts` 回归验证。

![手机页面预览](../output/visual-review/all-pages/mobile-overview.png)

## Requirement coverage

新增／补齐用例在 `web/tests/browser/react/pages.spec.ts`，使用真实列表／夹具；缺失值及 Payload 边界用独立 HTTP mock 验证显示，不放宽业务规则。

| Requirement | Evidence |
| --- | --- |
| 14 个页面／子面板在桌面和手机中保留内容与操作；隐藏字段不可见 | `page layout <surface> at <width> keeps content and actions inside the viewport`，14 个具名 surface × 1280／390 共 28 项，检查页面宽度、关键入口、可编辑金额、17 字段库和隐藏值。 |
| 模板分类／关键词组合、无匹配空态、草稿继续编辑与数据恢复 | `application templates combine category and keyword filters and keep draft resume reachable` |
| 流程名称／Key 搜索保留服务端分类过滤 | `definition keyword search matches names and keys without discarding the server category filter` |
| 后端百分数直接展示，缺失值保持未知 | `statistics renders authoritative percentage units and keeps a missing approval rate unknown` |
| 小控件与筛选区的实际圆角 | `the scoped theme renders rounded small controls and filter surfaces` |
| Payload 中文／层级、false／null／0 保真、精确原文、非 JSON 内容及窄屏关闭 | `outbox payload presents readable JSON and preserves original or invalid content at 1280` 和 `outbox payload presents readable JSON and preserves original or invalid content at 390` |

审批动作、恢复、草稿、人员权限、完整字段、拖拽／连线／分支／撤销重做／节点插入／自动布局、Action 和模拟执行由现有完整浏览器用例继续验证，包括 `approval.spec.ts`、`reliability.spec.ts`、`designer-interactions.spec.ts`、`actions.spec.ts`、`simulation.spec.ts`、`certificates.spec.ts`。

## 实际验证

| 命令 | 结果 | 证据 |
| --- | --- | --- |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore` | 通过：223 个源码边界、15 项边界／声明测试、320 项单元测试、类型检查、Vue／React 库与 Demo 构建 | `output/visual-review/all-pages/final-frontend.log` |
| 在 `web/` 执行 `pnpm contracts:check` | 通过，与全部已签入契约基线一致 | `output/visual-review/all-pages/final-contracts.log` |
| `pnpm test:e2e:react pages.spec.ts -g 'outbox payload'` | 通过 2/2，skipped／unexpected／flaky 均为 0 | `output/playwright/react-1794034379441/results.json` |
| `pnpm verify:react-package` | 通过：实际 tgz 独立安装、类型／构建、native ESM 和无 Vue 依赖检查 | `output/engineering/react-consumer-1864791f-6092-42fc-855e-58732d15bf9f/verification.json` |
| `pnpm test:e2e:react` | 通过 55/55，skipped／unexpected／flaky 均为 0 | `output/playwright/react-1794034801768/results.json` |
| `pnpm test:e2e:packed` | 通过 55/55，skipped／unexpected／flaky 均为 0 | `output/playwright/react-1794034971267/results.json` |

所有本轮运行均验证当前未提交工作区，不能作为 GitHub Actions 对某个提交的验收。未运行 .NET 单测及 MySQL／SQL Server／PostgreSQL CI 矩阵；浏览器宿主构建和 SQLite 场景不能替代这些测试。

本轮未提交、推送、发布、启动 Docker 或修改开发数据库。截图位于忽略的 `output/`，本地可查看；全页面自动截图由现有浏览器脚本重新生成到各次 `output/playwright/react-*`。临时浏览器视口已恢复，验收用页面已关闭；已核对进程归属并停止前一聊天留下的隔离宿主／预览（63289／63290），保留其 SQLite、日志和截图。

已知提示：Action 代码编辑器仍有既存的 Vite 大 chunk 提示；本轮没有为消除提示调整打包语义或加入新基础设施。

## 可复制验证命令

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore
Set-Location './web'
pnpm 'contracts:check'
pnpm 'test:e2e:react'
pnpm 'verify:react-package'
pnpm 'test:e2e:packed'
```
