# React 设计系统（shadcn/ui）

基础 UI 使用官方 shadcn/ui Radix Nova 组件，保留 React 19 与 Tailwind CSS v4。组件由官方 CLI 加入 `web/packages/approval-react-ui/src/components/shadcn/`；现有 `components/ui/primitives.tsx`、`extended.tsx`、`DataTable.tsx` 是公共 API 的薄适配层。

## 组件来源与职责

- Button、Input、Select、Textarea、Checkbox、Switch、RadioGroup、Field / FieldGroup、Card、Table、Accordion、Item：官方组件负责样式与控件语义。Select 使用 Radix 弹层，不再使用 NativeSelect。
- Dialog、AlertDialog、Sheet、Popover、DropdownMenu、Tooltip：Radix 负责焦点、键盘、Portal、关闭与定位。
- DatePicker 由官方 Calendar + Popover + InputGroup 组成，保留键入、清空、禁用和日期边界；日期传值保持原 YYYY-MM-DD。角色／Action 路径建议使用 Command + Popover，允许输入宿主自定义值。Command 使用 cmdk，Toast 使用 Sonner。
- Demo 导航使用官方 Sidebar、Breadcrumb、Avatar、Separator。
- 业务图、字段 Schema、公式、命令恢复、权限、审批与后端规则继续使用原共享实现。

当前选择组件目录中的 34 个组件文件（Sidebar 另有一个 use-mobile hook）。新增常用 UI 先检查官方组件，不再扩充手写控件样式。

## 主题与宿主隔离

`components.json` 明确 style=radix-nova、base=radix、iconLibrary=lucide、Tailwind prefix=approval。`src/styles.css` 加载官方 shadcn/tailwind.css 状态变体与 tw-animate-css，并以语义 token 定义主题：

```css
.approval-react {
  --primary: oklch(.53 .19 264);
  --background: oklch(.972 .004 265);
  --card: oklch(1 0 0);
  --foreground: oklch(.23 .025 265);
  --border: oklch(.91 .008 265);
  --ring: var(--primary);
}
```

不要用旧 --ar-primary 等变量覆盖通用组件外观；旧 ar 变量只供业务画布 / 布局适配。基础重置在 base layer 内且限定 `.approval-react`，不导入全局 Preflight。打包 CSS 已包含所需 utilities、状态变体与动画，宿主无需安装 Tailwind 或 shadcn CLI。

组件的颜色、字体、圆角和状态使用官方 variants / tokens；className 用于宽度、间距、布局。UI 中图标使用项目的 lucide-react。

## 组合与可访问性

Card 使用 CardHeader / CardTitle / CardContent；统计也由官方 Card 组成。字段使用 Field / FieldTitle / FieldDescription / FieldError，关联 aria-invalid / aria-describedby，保留原错误及输入值。Select 使用 `onValueChange(value)`，size 为 `default` / `sm`，清空时返回空字符串；通用 React Select 的 props 本轮有意调整，全部仓库消费者已同步。

原生 details 已换为 Accordion，使用 forceMount 和关闭时隐藏，保留未提交的输入缓冲。审批轨迹、附件和申请步骤使用 Item / ItemGroup；状态 Badge 的 success / warning / info 是基于语义 token 的官方组件 variants，不再依赖未分层 CSS 覆盖。

普通任务 Dialog 和需要确认的 AlertDialog 分开；确认命令失败时保持窗口打开，取消后恢复原触发焦点。busy Spinner 隐藏于可访问名称，确认按钮的名称和禁用语义保持稳定。选项在对应 Group 中，TooltipProvider 由 ApprovalProvider 安装。

人员搜索使用官方 Command + Popover，保留服务端目录、排除人员、失效状态、多选去重和身份切换清理；搜索输入具有 combobox 语义，列表保留“人员搜索结果”标签。

## 审批工作台产品布局

个人工作台采用页头、真实任务摘要、分类导航、筛选与列表；详情采用申请内容与申请信息／轨迹双栏。深色 Sidebar 使用 sidebar 语义 token，浅灰 background 与白色 card 区分页面和内容。手机保留业务字段，列表转为任务卡片，长标题和编号换行。资料与验收见 `docs/react-workbench-report-2026-11-07.md`。

其余页面使用 PageHeader、带标签的筛选区、关联信息归组的 DataTable 和明确的空态。发起中心优先展示申请模板，草稿放在独立 Tab；流程定义支持名称／Key 与服务端分类组合筛选；监控、诊断使用真实服务端指标。通过率直接展示后端百分数，空值保留未知。

设计器保留节点库、画布／表单和属性三个工作区，窄屏依次排列；原有拖拽、连线、撤销重做、Action、公式、明细表和模拟交互保留。Outbox Payload 展示格式化内容并提供原文，无法解析的内容直接展示原文。逐页桌面／手机截图和验收见 [React 全页面报告](react-all-pages-report-2026-11-07.md)。

## 更新官方组件

在 `web/packages/approval-react-ui/` 中运行：

```powershell
pnpm dlx shadcn@latest info --json
pnpm dlx shadcn@latest docs button
pnpm dlx shadcn@latest add button --dry-run
pnpm dlx shadcn@latest add button --diff
```

先审阅差异再更新。保留项目必要的小适配：package-relative imports、Portal 的 approval-react 主题容器、中文导航名称和语义状态 variants。CLI 更新后运行 `node scripts/shadcn-imports.mjs` 规范化导入，发布声明不依赖内部 @/ 别名。库构建将声明依赖交给宿主应用构建，避免 require(react) 留在发布文件中。

## 验证

`pnpm check:ui:react` 对 42 个页面／组件消费者执行 JSX AST 检查，阻止裸控件、details、datalist 和原生日期输入回流；`pnpm test:ui:react` 检查拒绝规则与字符串／注释误报。两者纳入 Frontend 门禁。完整逐页替换、交互与打包结果见 [框架 UI 验收](react-framework-ui-report-2026-11-07.md)。

本轮新增字段错误 ARIA、原生选择清空 / disabled、失败确认窗口和焦点回归；原日期边界、键盘搜索、单选组隔离及 16 字段用例保留。完整前端与实际 tgz / 浏览器结果见 `docs/react-shadcn-report-2026-11-07.md`。手机导航 Escape、状态、默认和 390px 布局已在浏览器复核，临时视口已恢复。
