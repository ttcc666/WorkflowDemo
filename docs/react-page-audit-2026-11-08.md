# React 逐页浏览器审查 · 2026-11-08

当前 React 页面已经大量使用 shadcn/ui。最值得继续改进的是日期筛选、手机工作台标签、模拟状态文案，以及公共表单组件的可访问性。单纯继续替换原生标签，不能解决这次复现的问题。

后续状态：用户要求全部修复后，A1–A7、B1–B4 及逐页细节已实施。结果与重新验证记录见 [修复完成记录](D:/Study/CSharp/Workflow/docs/react-audit-fixes-2026-11-08.md)；以下内容保留为修改前的审查证据。

本轮是审查与分析，没有修改应用源码、公共 API、HTTP/JSON/TypeScript 契约或数据库模型，也没有提交、推送。

## 审查范围与证据

- 用用户指定的 Playwright CLI 技能，在 Microsoft Edge 中检查全部 10 个业务路由；另外检查导航、通知、404、权限不足及详情不存在的状态。
- 桌面窗口为 1280×900，手机窗口为 390×844。出现滚动条时，实际内容宽度分别约为 1265、375；弹窗锁定背景滚动时宽度会变化。
- 开发站点 `http://localhost:5174` 只读查看。其余操作在独立 SQLite 宿主中完成：API `127.0.0.1:57500`，React production preview `127.0.0.1:57501`。
- 隔离库准备了 17 字段的完整动态表单、复杂流程与 Action、运行中/已退回/已通过实例、抄送、草稿、注册证历史及两个可对比版本。样本准备通过真实 API 创建、批准和退回隔离实例；这些准备操作不计为 UI 流程验证。没有在开发数据库提交审批、注销证书或标记消息已读。
- 共保留 **101 组页面/弹窗状态截图和 snapshot**，其中 98 组有常规布局指标。它们是审查证据数量，不是测试用例通过数量。
- 分支为 `main`，HEAD 为 `27588b1b00d36e9c91e8735094698625f30fcacc`；工作区包含此前已有的大量未提交修改。本报告针对当前工作区，不代表该干净提交或 CI 的验证结果。

完整证据目录：[review-20261108](D:/Study/CSharp/Workflow/output/playwright/review-20261108)。[证据索引](D:/Study/CSharp/Workflow/output/playwright/review-20261108/inventory.json)包含截图、快照及指标路径；[隔离数据上下文](D:/Study/CSharp/Workflow/output/playwright/review-20261108/context.json)记录本轮实例和数据库位置。

## 每页分析

| 页面 / 区域 | 实际检查及框架使用 | 结论与建议 | 主要截图 |
| --- | --- | --- | --- |
| 全局导航与消息 | Sidebar、手机 Sheet、Breadcrumb、Avatar、Select、消息 Dialog；检查导航展开、Escape 关闭、消息列表 | 手机导航可用。补齐首次路由加载反馈；通知长列表可受益于固定底部操作区。没有执行已读操作。 | [手机导航](D:/Study/CSharp/Workflow/output/playwright/review-20261108/mobile-sidebar-dialog.png)、[通知](D:/Study/CSharp/Workflow/output/playwright/review-20261108/message-dialog.png) |
| `/workbench` 审批工作台 | 四个分类均查看；待办、已处理、我发起、抄送；使用 Card、Tabs、Badge、Table、分页及筛选组件 | 桌面信息组织清晰，手机表格转为记录卡片。最右分类的宽度与滚动容器有缺陷，见 A2；字段标题关联也应同步改进。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/workbench-desktop.png)、[已处理](D:/Study/CSharp/Workflow/output/playwright/review-20261108/workbench-done-mobile.png)、[我发起](D:/Study/CSharp/Workflow/output/playwright/review-20261108/workbench-mine-mobile.png)、[抄送](D:/Study/CSharp/Workflow/output/playwright/review-20261108/cc-workbench-mobile.png) |
| `/approvals/:id` 审批详情 | 长标题、完整字段、可编辑金额、流程图、抄送历史、Action 空记录、审计轨迹；五种审批 Dialog 和重新提交 Dialog | 申请内容与轨迹的层次合理；Dave 页面未显示该节点隐藏的 secret 字段。操作弹窗可以关闭，同意弹窗关闭后焦点回原按钮。长表单重新提交需要改进滚动布局，见 B2。未提交审批动作。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/detail-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/detail-mobile.png)、[退回](D:/Study/CSharp/Workflow/output/playwright/review-20261108/approval-退回-dialog.png)、[重新提交](D:/Study/CSharp/Workflow/output/playwright/review-20261108/resubmit-visible-viewport.png) |
| `/start` 选择申请 | 搜索、分类、申请入口；Card、Badge、InputGroup、Select、Tabs | 内容易于扫描，桌面与手机布局正常。流程说明较长时可以按需折叠，保留名称、分类与入口。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/start-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/start-mobile.png) |
| `/start` 我的草稿 | 草稿列表、继续编辑、删除确认；Table、AlertDialog | 继续编辑能加载已保存值；删除确认可以取消。未删除或重新保存该草稿。可补充草稿摘要以区分同流程的多个草稿。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/drafts-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/drafts-mobile.png)、[恢复编辑](D:/Study/CSharp/Workflow/output/playwright/review-20261108/draft-resume-desktop.png) |
| `/start/:key` 动态表单申请 | 17 种字段覆盖文本、数值、公式、日期、日期区间、选择、人员、明细、附件等；Field、Input、Calendar/Popover、RadioGroup、Switch、Command 等 | 无整页横向溢出，提交区会保持可见。存在字段标签/必填语义和人员展开语义问题，见 A4/A5。明细在手机上横向滚动，可增加提示。未上传附件或提交申请。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/request-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/request-mobile.png)、[人员搜索](D:/Study/CSharp/Workflow/output/playwright/review-20261108/user-picker-open.png) |
| `/definitions` 流程定义 | 列表与筛选；新建、复制、导入 Dialog；Table、Field、Input、Select | 已采用框架组件，手机记录布局正常。新建/导入/复制只查看弹窗，不执行创建或导入。可给新增流程所需信息加更明确的必填及示例说明。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/definitions-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/definitions-mobile.png)、[导入](D:/Study/CSharp/Workflow/output/playwright/review-20261108/definition-import-dialog.png) |
| 流程版本、治理、审计 | 版本详情、治理、审计、版本对比；Dialog、Tabs、Select、Table、Accordion | v1/v2 对比正确展示一个字段标签变化和一个节点名称变化，并提供节点定位入口。手机对比可阅读。原始路径适合保留，同时可补充中文变化摘要。未执行启停或历史版本选用。 | [版本](D:/Study/CSharp/Workflow/output/playwright/review-20261108/definition-versions-dialog.png)、[治理](D:/Study/CSharp/Workflow/output/playwright/review-20261108/definition-governance-dialog.png)、[审计](D:/Study/CSharp/Workflow/output/playwright/review-20261108/definition-audit-dialog.png)、[手机对比](D:/Study/CSharp/Workflow/output/playwright/review-20261108/definition-compare-mobile.png) |
| `/designer/:key` 流程设计 | 节点库、复杂条件/并行/汇聚画布、通知区域；React Flow 配合 shadcn Card/表单/工具栏；预览及模拟 Dialog | 画布使用专用库是合理选择。模拟状态存在业务文案误导，见 A3。手机可查看，完整编辑仍需大量上下滚动；节点属性也适合使用 Sheet。未修改保存或发布流程。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-flow-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-flow-mobile.png)、[预览](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-preview-dialog.png)、[模拟](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-simulation-result.png) |
| 设计器 Action 配置 | JavaScript、SQL 查询、Mapping、宿主 Action、HTTP 配置；CodeMirror、Field、Input、Select、JSON 编辑适配 | 配置组件已经接入框架。SQL 采用宿主登记查询，HTTP 采用宿主登记目标；本轮仅查看配置及模拟计划。步骤上移/下移按钮目前读作箭头，可补充动作名与步骤名。脚本编辑器及格式化器已有懒加载。 | [JavaScript](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-action-steps.png)、[SQL](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-sql.png)、[Mapping](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-mapping.png)、[HTTP](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-http.png) |
| 设计器表单 | 17 字段卡片、属性区、字段库；Card、Field、Select、Checkbox | 桌面布局可用，但中心是字段结构列表，真正的填表效果需要切换预览。手机属性区位于所有字段之后，见 B1。可保留结构列表并改善预览与属性编辑的切换。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-form-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-form-mobile.png) |
| 设计器发起设置 | 展示开关、分类、说明、角色、标题模板变量；Switch、Checkbox、Field | 桌面与手机无明显溢出。角色限制及模板说明齐全；仍受 A4 的公共字段语义问题影响。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-settings-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/designer-settings-mobile.png) |
| `/monitor` 全部流程 | 列表、筛选、授权终止入口；Table、Badge、日期组件 | 接口 503 时显示失败反馈，取消拦截后点重试能恢复。残缺日期本次显示中文提示并保留原列表，未复现统计页同样的英文异常。未执行终止。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/monitor-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/monitor-mobile.png)、[503](D:/Study/CSharp/Workflow/output/playwright/review-20261108/monitor-network-failure.png)、[恢复](D:/Study/CSharp/Workflow/output/playwright/review-20261108/monitor-network-recovered.png) |
| `/monitor` 统计 | 汇总指标、流程/节点统计、积压；Card、Metrics、Table、DatePicker | 存在 A1 日期编辑异常。手机统计表转为记录展示后很长；修复筛选后可考虑按需折叠细分统计，优先显示总览。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/statistics-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/statistics-mobile.png)、[日期异常](D:/Study/CSharp/Workflow/output/playwright/review-20261108/statistics-partial-date.png) |
| `/monitor` 离职交接 | 人员选择、空预览、原因与确认入口；Command/Popover、Table、Textarea | 页面说明清楚，手机排版正常；人员组件存在 A5。未选择人员时显示“总数未知”的分页没有操作价值，可在预览有数据后展示。未执行交接或部分提交恢复。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/handover-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/handover-mobile.png) |
| `/outbox` 投递记录 | 投递列表、关联审批、Payload Dialog、宿主 Webhook 面板；Table、Dialog、Accordion、Select | Payload 可以打开。主投递状态已有中文，事件类型/目标仍直接显示 Host、Completed 等。手机记录信息密度较低，见 B3。未执行真实重试或配置 Webhook 失败次数。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/outbox-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/outbox-mobile.png)、[Payload](D:/Study/CSharp/Workflow/output/playwright/review-20261108/outbox-payload-dialog.png) |
| `/diagnostics` 运行诊断 | 正常摘要、风险列表、关联审批、Worker 详情；Alert、Metrics、Table、Accordion | 初始正常状态和后续实际出现的 unavailable_assignee 风险行均查看，资源链接存在。应给问题代码补中文解释；保留原代码便于定位。风险判定本身的业务准确性未在本轮验证。 | [正常桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/diagnostics-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/diagnostics-mobile.png)、[风险及 Worker](D:/Study/CSharp/Workflow/output/playwright/review-20261108/diagnostics-worker-details.png) |
| `/certificates` 注册证业务 | 列表、登记/变更、历史、申请详情、注销确认、运行检查；Table、Dialog、AlertDialog、DatePicker | 手机证书卡片及历史详情可阅读。注销确认存在 A6 必填无反馈问题。登记/变更/历史弹窗已查看，未提交登记、变更或注销。 | [桌面](D:/Study/CSharp/Workflow/output/playwright/review-20261108/certificates-desktop.png)、[手机](D:/Study/CSharp/Workflow/output/playwright/review-20261108/certificates-mobile.png)、[登记](D:/Study/CSharp/Workflow/output/playwright/review-20261108/certificate-register-mobile.png)、[历史](D:/Study/CSharp/Workflow/output/playwright/review-20261108/certificate-history-mobile.png)、[申请详情](D:/Study/CSharp/Workflow/output/playwright/review-20261108/certificate-request-mobile.png) |
| 错误与无权限页面 | 不存在的路径、无 Outbox 权限、不存在的审批 | 权限不足和详情不存在有中文反馈；未匹配路由显示默认开发错误页面，见 A7。可统一补充返回工作台入口。 | [404](D:/Study/CSharp/Workflow/output/playwright/review-20261108/route-404.png)、[无权限](D:/Study/CSharp/Workflow/output/playwright/review-20261108/outbox-permission-denied.png)、[详情不存在](D:/Study/CSharp/Workflow/output/playwright/review-20261108/detail-not-found.png) |

## 优先修复：已确认问题

以下 P2 表示会影响日常输入、信息理解或可访问性的缺陷；本轮没有证据表明存在 P0/P1 的审批写入、权限或数据损坏问题。

### A1 · P2 · 统计页在日期编辑过程中报英文异常

复现：管理员进入流程监控的“统计”，在“统计开始日期”输入 `2026-`。页面出现 `Invalid time value`，统计区域不再展示正常数据；清空后可恢复。

原因：[MonitorPanels.tsx:146](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/features/monitor/MonitorPanels.tsx:146)直接把每次输入变化交给查询，在完整日期之前调用[localDayStart](D:/Study/CSharp/Workflow/web/packages/approval-shared/src/features/lists/listModel.ts:24)，其 `toISOString()` 会对无效日期抛异常。DatePicker 的编辑/失焦提示不能代替查询入口校验。

建议将正在编辑的筛选值与已生效查询分开；完整日期、真实日历日期及区间顺序校验通过后再查询。非法输入应显示中文字段提示，并保留最近有效结果。本轮**没有**在监控列表复现同样的英文异常，不能把该页归入已确认失败范围。

证据：[snapshot](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/statistics-partial-date-snapshot.txt)、[恢复截图](D:/Study/CSharp/Workflow/output/playwright/review-20261108/statistics-reset.png)。

### A2 · P2 · 手机工作台的标签容器超出卡片宽度

390px 手机窗口下，最右“抄送我的”位于可见范围之外。用键盘 End 可以选中它，但焦点标签仍在屏幕外。CLI 点击该标签自动把目标滚入时，发生滚动的是整个审批列表卡片，筛选与记录左侧随之被裁切。

指标进一步定位到：标签容器宽约 437px，而卡片内容宽 347px；标签容器自己的 `scrollLeft=0`，祖先 Card 却出现 `scrollLeft=106`、`overflow-x:hidden`。这是卡片内部的宽度问题；整页 `scrollWidth` 仍等于视口宽度。因此只检测整页横向溢出会漏掉它。键盘路径与 CLI 点击路径的表现不同，不能把两者混为一次复现。

原因集中在[WorkbenchPage.tsx:605](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/pages/WorkbenchPage.tsx:605)的 CardHeader → Tabs → 滚动条组合及[styles.css:608](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/styles.css:608)。建议约束 grid 子项/Tabs 根节点的最小宽度，把横向滚动限定在标签条，并让键盘选中的标签滚到该条的可见范围内。

![手机标签的两条复现路径](D:/Study/CSharp/Workflow/output/playwright/review-20261108/workbench-tabs-evidence.png)

证据：[键盘指标](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/mobile-keyboard-scroll-metrics.txt)、[点击后的祖先滚动指标](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/workbench-pointer-scroll-metrics.txt)。

### A3 · P2 · 模拟计划阻塞被显示成“已拒绝”

执行完整流程模拟后，JavaScript 步骤产生预演输出；SQL 步骤只展示查询计划，没有正式输出，后端返回 `Blocked`，原因为“仅显示动作计划，未产生正式输出”。流程图却将该 Action 节点标为“已拒绝”。这不是审批拒绝。

[simulationState.ts:9](D:/Study/CSharp/Workflow/web/packages/approval-shared/src/features/flow/simulationState.ts:9)把 `Blocked` 和 `Failed` 都映射为 `rejected`，[FlowViewer.tsx:49](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/features/flow/FlowViewer.tsx:49)再使用真实实例的“已拒绝”文案。

建议给预演的阻塞、失败、仅计划状态提供独立文案，并显示阻塞原因。不要为让模拟继续而把计划输出当成真实动作输出。若调整共享状态类型，需同步 Vue 消费者。

证据：[模拟 snapshot](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/designer-simulation-result-snapshot.txt)。

### A4 · P2 · 公共 Field 缺少标题与控件关联及必填语义

完整申请中“产品编码”显示必填星号，但实际输入框没有 `required` 或 `aria-required`，`labels.length=0`。点击标题后焦点仍为 BODY，输入框没有获得焦点。输入框已有 `aria-label`，可读名称本身并未缺失。

[primitives.tsx:225](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/components/ui/primitives.tsx:225)用 FieldTitle 展示标题，FieldState 只传播错误及帮助说明，没有传播控件 ID 与必填状态。部分筛选页直接使用 FieldLabel，但也未关联 `htmlFor`。

建议单控件 Field 使用 FieldLabel/htmlFor/id，并给控件传播 `aria-required`；日期区间、明细等多控件分组使用 FieldSet/FieldLegend 和各自标签，避免把一个标题错误地关联到多个控件。

证据：[输入属性](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/request-accessibility-closed.txt)、[点击标题后的焦点](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/product-label-focus.txt)。

### A5 · P2 · 人员选择器关闭后仍报告展开

未打开弹层时，“单人人员”和“多人人员”都是 `aria-expanded=true`；其 `aria-controls` 指向当时不存在的列表。搜索 Bob 后，通过 ArrowDown、Enter 选择成功，弹层关闭，但展开属性仍为 true。

原因是[UserPicker.tsx:105](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/components/UserPicker.tsx:105)的 CommandInput 没有与受控 Popover 的 open 状态同步展开语义。

建议同步 `aria-expanded` 与实际 open 状态，并保证关闭时对列表的引用有效或被移除。人员搜索、键盘选择已验证可用；此问题主要影响辅助技术对状态的判断。

证据：[选择后 snapshot](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/user-picker-selected-snapshot.txt)、[选择后属性](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/user-picker-selected-metrics.txt)。

### A6 · P2 · 注销原因为空时确认按钮无反馈

注册证列表打开“申请注销”，不填写原因即可点击“确认”。点击后弹窗不关闭、没有字段错误，确认按钮仍可点击；本轮网络记录没有产生注销写请求。

[CertificatesPage.tsx:87](D:/Study/CSharp/Workflow/web/apps/react-demo/src/pages/CertificatesPage.tsx:87)在原因为空时直接 return；公共 ConfirmDialog 的确认按钮只考虑 busy，没有缺少原因时的禁用或校验反馈。

建议补充必填字段错误并聚焦原因输入，或者禁用确认并提供明确说明。保留现有服务端授权及写请求保护。

证据：[点击后 snapshot](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/certificate-revoke-no-feedback-snapshot.txt)、[请求记录](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/certificate-revoke-requests.txt)。

### A7 · P2 · 未匹配路由显示默认开发错误页面

访问 `/review-page-does-not-exist`，production preview 显示 `Unexpected Application Error!`、`404 Not Found` 和 `Hey developer`，没有应用导航或返回入口。

[main.tsx:263](D:/Study/CSharp/Workflow/web/apps/react-demo/src/main.tsx:263)的路由树没有自定义未匹配页面/错误边界。建议增加中文 NotFound 与路由 ErrorBoundary，并提供返回工作台及重试入口。没有在本轮验证其他运行时异常是否会展示堆栈。

证据：[404 snapshot](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/route-404-snapshot.txt)。

## 后续体验优化

### B1 · 手机设计器用 Sheet 编辑属性

17 字段的手机表单设计页面约 4894px 高，属性面板排在全部字段卡片之后。选中顶部字段时，属性区域仍在底部。建议复用已有 Sheet：选择字段或节点后打开属性面板，保存本地编辑后关闭；桌面保留三列布局。关联：[FormDesigner.tsx:852](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/features/forms/FormDesigner.tsx:852)、[手机断点](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/styles.css:1682)。

### B2 · 长 Dialog 固定标题和操作区

手机重新提交弹窗可视高度约 792px，内容滚动高度约 2003px；操作区初始位置约 y=1916，位于 844px 视口之外。滚动仍能到达按钮，问题是确认/取消和关闭入口不能同时保持可见。

建议在[公共 Dialog](D:/Study/CSharp/Workflow/web/packages/approval-react-ui/src/components/ui/primitives.tsx:286)中采用受限高度的容器，只让中间内容滚动，固定标题与 footer；窄屏长表单可采用全屏 Dialog/Sheet。证据：[布局指标](D:/Study/CSharp/Workflow/output/playwright/review-20261108/logs/resubmit-mobile-metrics.txt)、[可见视口](D:/Study/CSharp/Workflow/output/playwright/review-20261108/resubmit-visible-viewport.png)。

### B3 · 管理列表保留关键摘要，按需展开技术详情

Outbox 手机样例列表在 16 条记录时页面高度约 6223px。可优先展示中文事件名称、状态、时间与关联审批，把长 ID、下次尝试等次要信息放到展开区。诊断问题代码也可同时提供中文解释与可复制原代码。模拟结果建议先展示路径、阻塞原因和动作结果，原始 JSON 放在 Accordion 中。

这些区域已使用 Table/Card/Dialog/Accordion，适合改善组合与信息密度，不需要更换 UI 框架。

### B4 · 整理样式职责，并补齐初次加载反馈

保留页面网格、图形画布及动态 span 所需的布局样式。优先清理重复规则、对框架内部结构的脆弱覆盖，并把流程节点中的硬编码状态颜色收敛为语义 token；无需机械地把每条 CSS 都改成 Tailwind。

本轮控制台确认 `No HydrateFallback element provided...` 警告；开发站点另有 favicon 404。建议补齐初次路由加载反馈与图标。路由、脚本编辑器、Prettier 已有懒加载，不应再次当作“缺少懒加载”提出改造。较大的脚本编辑器 chunk 只构成进一步测量的线索，本轮没有做慢网性能基准。

## 验证结果与限制

| 本轮实际执行 | 结果 |
| --- | --- |
| `node scripts/react-e2e.mjs --serve`，在 `web/` 执行 | 隔离宿主 Release 构建与 React production build 成功；使用 serve 模式，没有运行 Playwright test specs。日志：[宿主构建](D:/Study/CSharp/Workflow/output/playwright/react-1794107865391/host-build.log)、[React 构建](D:/Study/CSharp/Workflow/output/playwright/react-1794107865391/react-build.log)。 |
| `node web/scripts/react-ui-audit.mjs`，在仓库根执行 | 42 个消费组件文件检查通过；官方 shadcn 组件目录外没有脚本禁止的原生控件、原生日期 Input 或 datalist。它不能证明组合方式、ARIA 或视觉交互正确。 |
| Playwright CLI 浏览器审查 | 101 组状态证据；包含响应式布局、弹窗、键盘人员选择、标签焦点、503/重试、404、权限/不存在反馈。 |
| 单元测试、.NET 测试、Provider/CI、包消费者 | 本轮没有执行，也没有复用之前的绿色结果作为本轮验收。没有新增测试文件。 |

浏览器审查没有提交审批、删除草稿、执行流程启停/发布/导入、批量交接、证书注销、消息已读或附件上传下载；也没有调用真实外部 SQL/HTTP 服务。隔离样本准备涉及真实审批 API 写入，不能因此声称相应 UI 提交流程已验证。登记、变更、注销、到期等全部业务状态组合没有逐一准备。本轮验证的是已列出的页面呈现与交互；它不能替代这些业务流程的回归测试。

手机检查是 Edge 窗口模拟宽度及键盘操作，不是实体手机触摸测试。可访问性结论来自真实 DOM 属性、焦点和键盘行为，没有用读屏软件做完整验收。

可复制的静态复核命令：

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
node 'web/scripts/react-ui-audit.mjs'
```

重新启动一个隔离浏览器宿主：

```powershell
Set-Location 'D:\Study\CSharp\Workflow\web'
node 'scripts/react-e2e.mjs' '--serve'
```

该脚本会生成新的端口和隔离数据库。本轮 seed 脚本绑定了本轮数据库及端口，不能直接对新环境重跑；其路径检查用于避免写入开发数据库。保留本轮 SQLite 和证据文件便于追溯。

建议修复顺序：**日期查询入口 → 手机标签宽度 → 模拟文案 → Field/UserPicker 可访问性 → 注销必填与 404 → 手机属性面板和长弹窗**。前六类应做小范围修复，保留现有审批业务、授权、幂等和事务语义。
