# React 逐页框架 UI 复查

2026-11-07，本地未提交工作区，基于 HEAD `27588b1b00d36e9c91e8735094698625f30fcacc`。本轮针对原生控件和手写通用 UI 的残留作实际替换，补充上一轮侧重布局／业务回归的验收。

## 实际问题与修改

- 旧 `Select` 虽调用官方 NativeSelect，交互仍是系统下拉；已替换为官方 Radix Select。`components/ui/primitives.tsx` 处理空值、ARIA、disabled 和独立实例，全部调用点改为 `onValueChange(value)`。删除已无消费者的 `native-select.tsx`。
- 旧 DatePicker 只是 `Input type=date`，Calendar 未接入业务页；现在由 Calendar、Popover 和 InputGroup 组成。动态表单、明细日期、工作台／监控日期筛选、证书表单均已接入，保留 ISO 日期、键入、清空、日期边界及禁用语义。
- 10 处 details 改为官方 Accordion：审批轨迹、版本 JSON、Outbox 原文、诊断详情、公式预览、明细列、通知规则、Mapping 选项和 Webhook 正文。forceMount 配合关闭时隐藏，保留未提交输入和错误。
- 2 处 datalist 改为 Command + Popover：宿主角色与 Action 源路径。保留自定义角色输入和路径拖入，修复搜索框的可访问名称。
- 手写统计块改为官方 Card；轨迹、附件、申请步骤和图标展示改为 Item／ItemGroup／ItemMedia；申请面包屑使用 Breadcrumb；字段错误使用 FieldError；审批禁用组使用 FieldSet；表单设计字段卡片使用 CardHeader／CardTitle／CardDescription／CardFooter。
- Badge 的状态颜色改为基于语义 token 的组件 variants，删除未分层 CSS 对 Badge 的覆盖。清理统计、步骤、面包屑、上传、字段卡片等旧外观样式。保留网格、移动表格、Schema 列宽、流程图 220×72 节点和必要业务展示样式。

官方 CLI 新增 Select、Accordion、Item，现有官方目录为 34 个组件文件。未增加另一套 UI 库。普通 HTML 内容、React Flow 画布、CodeMirror 编辑器及系统文件选择器保持各自必要职责。

## 逐页结果

| 页面／面板 | 框架组件与本轮处理 |
| --- | --- |
| 工作台 | Radix Select 筛选、官方 Table／Card／Tabs；摘要及任务图标使用 ItemMedia。 |
| 审批详情与动作 | Select、FieldSet、Item 轨迹、Accordion 详情；原同意／拒绝／退回／转交／加签等动作保留。 |
| 发起中心与草稿 | Select 分类、Card 模板、ItemMedia、Table 草稿；搜索／继续编辑保留。还有 |
| 动态申请 | DatePicker、Select、Breadcrumb、Badge、Item 步骤、附件 Item／Card 和 FieldError。 |
| 流程定义与版本治理 | Select 分类／版本、Accordion 定义 JSON；导入／复制／治理仍走原命令。 |
| 流程设计与 Action | 节点属性 Select，角色／路径建议 Command + Popover；Mapping、通知采用 Accordion；原画布、分支、撤销重做、预演保留。 |
| 表单设计 | Select 属性、Card 字段、Accordion 公式／明细列、DatePicker 日期预览；17 字段夹具保留。 |
| 发起设置 | 核对现有 FieldGroup／Switch／Checkbox／Input／Button 组合，保留角色、说明和标题变量。 |
| 实例监控 | Select 筛选、DatePicker 日期、官方 Table。 |
| 统计 | DatePicker 范围、Card 指标；后端百分数和未知值语义不变。 |
| 离职交接 | 原 Command 人员搜索、FieldGroup、Table、AlertDialog；没有回退为原生控件。 |
| Outbox／Webhook | Select 状态、Accordion 原文／正文、Table 和原重试／敏感读取授权。 |
| 运行诊断 | Card 指标、Accordion 心跳／宿主诊断、Table 风险、Alert 状态。 |
| 注册证及弹窗 | Select 类型、DatePicker 发证／到期日、Card 宿主指标、原 Dialog／AlertDialog／Table。 |

新增 `web/scripts/react-ui-audit.mjs` 使用 JSX AST 检查 42 个消费者，阻止裸控件、details、datalist 和直接原生日期输入回流。3 个守卫用例证明拒绝规则，同时验证 HTML 字符串／注释不误报。`eng/verify.ps1` 的 Frontend 门禁增加 `check:ui:react` 和 `test:ui:react`。原边界、类型和业务断言保留。

## 连线回归中发现的问题

首轮完整 tgz 浏览器运行 58/59；进一步使用 Playwright 自动等待拖动后，trace 明确显示审批节点拦截了结束节点端点。根因是点击添加按画布中心加偏移定位，可能覆盖已有节点。

`features/designer/designerModel.ts` 现在仅对点击添加、且没有指定插入边的节点避让已有矩形，保留既有节点位置；拖放仍落在精确指定的位置。新增单元用例同时检查避让、原节点位置不变、精确拖放与撤销重做。原默认位置用例继续保留。浏览器保留分段真实鼠标拖动，先等待两个端点可交互，并在释放前等待实际 connectingfrom／connectingto 反馈；两条连线、保存后的 JSON、审批人及 Vue 读取断言均未删除或放宽。没有加 sleep、增加 retries 或跳过场景。

## Requirement coverage

| Requirement | Evidence |
| --- | --- |
| 通用裸控件／原生日期不能回流，字符串／注释不会误报 | `rejects native widgets and native date inputs in consumer JSX`、`allows official component compositions and semantic content`、`does not treat JSON strings or comments as rendered native widgets` |
| Select 鼠标／键盘、原始值、清空、独立实例、禁用 | `Radix select supports keyboard values clear and independent controlled instances`、`Radix select closes an open popup when its command becomes disabled`、`keeps Radix select clear and disabled behavior through the official control` |
| 日期选择／清空、min／max、非法输入、禁用与区间独立 | `DatePicker uses a calendar popup and preserves canonical days and min max boundaries`、`DatePicker reports invalid or outside dates and closes its popup when disabled`、`DateRangePicker changes only the selected boundary and keeps the original tuple intact` |
| 角色／路径建议保留搜索、自定义值及禁用 | `SuggestionInput searches framework options and preserves custom host values`、`SuggestionInput closes its options when disabled without changing the host value`、`designer role suggestions use a framework search popup and retain custom host roles` |
| 折叠保留输入缓冲／错误并支持键盘 | `Accordion keeps an invalid mapping buffer and error while collapsed and reopened`、`designer Accordion hides and restores an invalid mapping buffer through keyboard interaction` |
| 指标使用 Card，零与未知值及标签正确对应 | `Metrics composes official cards and keeps zero and unknown values distinct` |
| 两种视口下日历／选择器保留值与弹层边界 | `framework select and calendar preserve input values and popup bounds at 1280`、`framework select and calendar preserve input values and popup bounds at 390` |
| 新节点不遮挡端点，拖放及历史保留 | `click-added nodes avoid existing endpoints while palette drops keep their exact position`、`React canvas connects approval nodes saves a real draft and Vue can edit the same definition` |

14 个页面／子面板 × 1280／390 的原布局检查增加实际 DOM 核对：没有 details、datalist、原生日期输入和可见系统 Select。原审批、字段权限、操作键恢复、流程／表单设计、Action、模拟及注册证业务回归完整执行。

## 实际验收

以下结果均为最终修复后的本地未提交工作区；已解析两份浏览器 JSON，确认各 59 项、unexpected／skipped／flaky 均为 0。

| 命令 | 实际结果与证据 |
| --- | --- |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore` | 通过：225 源码边界、42 消费者 UI 检查、3 UI 守卫测试、15 边界／声明测试、330 单元测试、类型与 Vue／React 库和 Demo 构建。`output/visual-review/framework-ui/final-frontend.log`。 |
| `pnpm verify:react-package` | 实际 tgz 独立安装、类型、构建、native ESM、无 Vue 依赖均通过。`output/engineering/react-consumer-bff661d1-186f-4222-a822-df8b03dc14b2/verification.json`。 |
| `pnpm test:e2e:react` | 最终完整 59/59。`output/playwright/react-1794046856453/results.json`。 |
| `pnpm test:e2e:packed` | 最终完整 59/59。`output/playwright/react-1794046997041/results.json`。 |
| `pnpm contracts:check` | 全部已签入基线匹配。`output/visual-review/framework-ui/final-contracts.log`。 |
| `pnpm test:e2e:react framework.spec.ts -g 'framework select and calendar'` | 静态效果图补充 2/2；截图关闭瞬时过渡动画，值与边界断言保留。`output/playwright/react-1794047545823/results.json`。 |

失败证据保留在 `output/playwright/react-1794043866922`、`react-1794044974621`、`react-1794045748504`；没有以它们或历史绿色结果代替最终验收。最终连线专项另在 `react-1794046391295/results.json` 通过。Select、Calendar 和建议弹层增加 16px 碰撞边距，手机日历的实际边界断言保留至少 8px 安全空间。

未执行 .NET 单测、MySQL／SQL Server／PostgreSQL CI 矩阵；不将浏览器宿主构建当作这些测试通过。没有提交、推送、发布、启动 Docker 或修改用户开发数据库。

## 截图

图片来自隔离 SQLite 数据与最终 UI，记录控件真实展开状态；日期、选择器和 Accordion 临时浏览器视口已恢复，验收宿主已停止。

![框架控件](../output/visual-review/framework-ui/framework-controls.png)

[桌面 1](../output/visual-review/framework-ui/desktop-pages-1.png) · [桌面 2](../output/visual-review/framework-ui/desktop-pages-2.png) · [桌面 3](../output/visual-review/framework-ui/desktop-pages-3.png)

[手机 1](../output/visual-review/framework-ui/mobile-pages-1.png) · [手机 2](../output/visual-review/framework-ui/mobile-pages-2.png) · [手机 3](../output/visual-review/framework-ui/mobile-pages-3.png)

## 契约与复现

通用 React Select 的 props 有意改为 `onValueChange(value)`，size 为 `default`／`sm`；全部仓库内消费者已同步，不保留未发布旧 API 的兼容层。额外导出 Accordion、FieldSet／FieldError、Metrics 等 UI 组件。HTTP／JSON／C# API、SDK 数据 DTO、数据库和审批规则均未改变。点击新增节点的位置有上述有意调整；拖放及已有流程位置保留。

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore
pnpm --dir './web' 'verify:react-package'
pnpm --dir './web' 'test:e2e:react'
pnpm --dir './web' 'test:e2e:packed'
pnpm --dir './web' 'contracts:check'
```

Action 编辑器仍有既存的大 chunk 构建提示；未为消除提示改变加载语义。
