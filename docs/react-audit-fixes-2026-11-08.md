# React 逐页审查修复完成记录

已处理 [逐页审查报告](D:/Study/CSharp/Workflow/docs/react-page-audit-2026-11-08.md) 的 A1–A7 和 B1–B4，并完成相关页面细节优化。没有提交或推送。

## 修复结果

| 项目 | 实际变化 | 验证 |
| --- | --- | --- |
| A1 日期筛选 | 统计页分离编辑值和已生效查询；明确刷新/重置；完整日历日期、范围顺序及可表达的查询边界校验。列表筛选也阻止非法日期提交。 | 残缺日期输入后统计 GET 请求仍为 **1 → 1**，保留有效结果并显示中文字段错误。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/statistics-negative-proof.json) |
| A2 手机工作台 | 约束 CardHeader/Tabs 的宽度；滚动限定在标签条；键盘选中标签自动进入可见范围。 | 最右标签可见，卡片 `scrollLeft=0`，标签条 `scrollLeft=105`。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/logs/final-tabs-proof.txt) |
| A3 模拟状态 | 区分“仅展示执行计划”“预演受阻”“预演失败”和真实审批状态；同节点重复出现时采用最新观察。 | SQL 计划阻塞显示“仅展示执行计划”，没有“已拒绝”。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/simulation-proof.json) |
| A4 表单可访问性 | 标签关联实际控件，传播必填和错误描述；复合字段使用 FieldSet/Legend；表格列采用各自的必填状态与错误 ID；筛选栏标签同步修复。 | 验证标签聚焦、唯一 ID、日期区间命名、表格必填/选填区别及错误关联；[浏览器属性检查](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/logs/request-controls-proof.txt)。 |
| A5 人员选择器 | cmdk Input 通过 asChild 与 InputGroupInput 组合；展开属性跟随弹层；关闭时移除无效列表引用；ArrowDown 可重新展开，Escape 先收起人员层。 | 键盘搜索/选择正常；收起人员层后审批 Dialog 仍在，确认按钮的原有条件保留。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/logs/transfer-users-closed-snapshot.txt) |
| A6 注销原因 | 空原因显示字段错误并聚焦输入，不进入原有命令执行器。填写原因后保持原来的操作键与提交流程。 | 空原因产生 **0 个 POST**。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/revoke-negative-proof.json)；有效路径有独立回归测试。 |
| A7 错误与加载页 | 增加中文 404、路由错误边界、返回入口和重新加载；配置 HydrateFallback；增加 SVG favicon。 | 404、模拟 chunk 503、取消拦截后重新加载均实际查看；最终正常页面控制台无错误/警告。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/logs/final-console.txt) |
| B1 手机属性编辑 | 字段与节点属性使用已有 Sheet 组件；桌面保留属性栏；手机提供明确编辑入口。 | 选中、编辑、关闭、重新打开后字段值保留。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/logs/field-retained-proof.txt) |
| B2 长弹窗 | 标题和 footer 固定，中间内容独立滚动；保留 Escape 与焦点恢复。 | 390×844 窗口中，弹窗高 812px，内容已滚动 1251px 时确认区底部仍为 y=828。[证据](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/logs/final-dialog-proof.txt) |
| B3 信息密度 | Outbox 手机摘要采用两列，技术信息按需展开；诊断代码补中文；模拟原始数据放入 Accordion；手机统计细项折叠。 | 同一 20 条 Outbox 数据从约 **7290px → 5452px**，缩短约 25%；完整信息及 Payload 保留。[展开截图](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/outbox-mobile-expanded.png) |
| B4 样式职责 | 整理重复 Tabs/入口规则，流程节点颜色使用语义 token，修复 Portal 根节点盒模型；保留既有懒加载。 | 44 个消费组件文件通过框架控件审查，独立 npm 消费者完成构建与浏览器验证。 |

其他逐页建议也已落实：草稿按需查看摘要、长申请说明折叠、新建/复制必填与示例、版本变化中文区域说明、明细横向滚动提示、交接未加载预览时隐藏分页、步骤上移/下移的可读按钮名称，以及无权限/详情不存在时的返回入口。

## 关键文件与设计取舍

- 公共控件：[primitives.tsx](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/components/ui/primitives.tsx)、[fieldContext.ts](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/components/ui/fieldContext.ts)、[UserPicker.tsx](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/components/UserPicker.tsx)、[Command 适配](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/components/shadcn/command.tsx)。采用框架组件组合，避免手工改写 DOM 的展开属性。
- 页面与设计器：[MonitorPanels.tsx](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/features/monitor/MonitorPanels.tsx)、[FormDesigner.tsx](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/features/forms/FormDesigner.tsx)、[DesignerPage.tsx](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/pages/DesignerPage.tsx)、[OutboxPage.tsx](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/pages/OutboxPage.tsx)、[styles.css](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/styles.css)。
- Demo：[RouterFeedback.tsx](D:/Study/CSharp/Workflow/web/apps/react-demo/src/features/RouterFeedback.tsx)、[CertificatesPage.tsx](D:/Study/CSharp/Workflow/web/apps/react-demo/src/pages/CertificatesPage.tsx)。增加了测试所需的开发依赖与 jsdom 配置。

React Field 增加可选 `group`/`className`，Textarea 增加 React 19 的 ref 支持。这些是组件能力扩展；没有修改 C# 公共 API、HTTP/JSON/审批 TypeScript DTO、数据库模型或审批业务规则。预演显示调整限定在 React 展示层，共享状态类型和 Vue 审批语义没有改变。

## 回归证据

新增 **9 个测试文件、27 个回归案例**，覆盖具体返回值、查询/命令参数、禁止请求、焦点与状态保留。没有删除或放宽原断言。

| Requirement | Evidence |
| --- | --- |
| 无效日期不查询、有效边界与重置 | `invalid statistics date 2026- keeps the last result and never queries`、`valid leap day statistics apply local inclusive bounds and reset both the query and inputs` |
| 筛选范围及重试 | `reversed statistics dates show field feedback without applying a query`、`statistics retry uses the applied query even when the editable date is incomplete` |
| 标签、必填、错误与复合字段 | `field labels focus the associated control and expose required and error descriptions`、`date range field groups keep both controls independently named and required`、`table cells expose their own required flags and validation description without inheriting the table requirement` |
| 筛选栏实际操作 | `list filters associate labels with inputs and block incomplete dates before searching` |
| 人员搜索及弹层状态 | `picker expanded state follows search selection and Escape while closed lists are not referenced` |
| 手机编辑保留 | `mobile field selection opens an inspector and edits survive closing and reopening it` |
| 模拟最新状态 | `simulation viewer renders the latest observation when a node is visited again`，另有 7 个状态/原因分区断言。 |
| 注销非法/有效路径 | `blank revoke reason shows a field error focuses the input and sends no command`、`valid revoke reason submits once with an operation key and opens the resulting request` |
| 页面恢复 | `not found feedback provides a working route back to the workbench`、`route failures show recoverable Chinese feedback without exposing internal error text` |
| 真实浏览器几何 | CLI `mobile-workbench-tabs`、`fixed-dialog-actions`、`mobile inspector edit retention`，直接检查可见范围与状态，不把 CSS 字符串当作布局验收。 |

既有浏览器用例有 5 处依赖旧 UI：整卡片唯一链接、手机常驻属性栏和自动统计刷新。已改为明确的申请入口、打开属性 Sheet 和点击刷新，保留原来的内容、数值、权限及存储结果断言。新增了属性值断言。

## 实际验证结果

| 命令 / 检查 | 结果与日志 |
| --- | --- |
| `pwsh -NoProfile -File ./eng/verify.ps1 -Scope Frontend` | 通过：类型、依赖边界、框架控件检查、**384 个前端测试**及 Vue/React 应用和库构建。[日志](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/frontend-final.log) |
| `pnpm contracts:check` | 与全部已签入基线匹配。[日志](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/contracts.log) |
| `pnpm test:e2e:react` | **63/63** 通过，真实 SQLite 宿主验证审批、表单、治理与业务流程。[日志](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/browser-regression.log) |
| `pnpm verify:react-package --skip-build` | 独立 tgz 消费者的安装、类型检查与构建通过。[最终日志](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/package-consumer-final.log) |
| `pnpm test:e2e:packed` | **63/63** 通过。[日志](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/browser-packed.log) |
| Outbox 最后一次密度调整 | 随后仅调整该页展示类名/CSS，重新构建库与应用、重建独立消费者；源码与打包版各 **4/4** 的布局/Payload 用例通过。[源码日志](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/outbox-regression-final.log)、[打包日志](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/outbox-packed-final.log) |
| Playwright CLI | **52 组** 页面/弹窗状态快照与截图；另有专门的可见范围、请求数量及焦点检查。[索引](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/inventory.json) |

本轮验证针对 `main` 的 HEAD `27588b1b00d36e9c91e8735094698625f30fcacc` 及其当前未提交工作区，不能当作该干净提交的 CI 结果。没有运行服务数据库 CI、Docker 专项或完整 .NET 单元测试；浏览器真实宿主使用隔离 SQLite。开发数据库未用于写入验证。

原有工作在修改前保留了文件副本，本轮差异可与 [baseline](D:/Study/CSharp/Workflow/output/playwright/fix-20261108/baseline) 比较。没有执行覆盖式 Git 恢复、提交、推送或发布。

可复制复核：

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend'
Set-Location 'D:\Study\CSharp\Workflow\web'
pnpm contracts:check
pnpm test:e2e:react
pnpm verify:react-package --skip-build
pnpm test:e2e:packed
```
