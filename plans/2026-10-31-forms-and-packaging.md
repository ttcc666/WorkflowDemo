# B 表单设计器 + 通用发起页 / C 封装为可复用包 — 开发计划

> 状态：**执行中**。阶段 0–4 已完成（每阶段完成后本地提交，不推送）。
> 执行时按“阶段 → 清单”逐项完成，每个阶段结束运行该阶段的验证命令；本文件的复选框随进度更新。

## 0. 背景与现状（执行前必读）

- 仓库：`D:\Study\CSharp\Workflow`，远程 `https://github.com/ttcc666/WorkflowDemo.git`，分支 `main`。
- 已推送的最后一次提交 `87e4a0c`；**并行分支 / 退回 / 加签 的改动（约 32 个文件）尚未提交**。执行前先问用户是否提交（不得擅自提交或推送）。
- 现有结构：
  - `src/Approval.Core`：引擎（.NET 10、EF Core 10.0.14、DynamicExpresso、Jint 4.17）。框架本体与证书无任何耦合。
  - `tests/Approval.Core.Tests`：xunit，76 个测试，`TestHost`（SQLite 内存库、`FakeTimeProvider`、`RecordingChannel`、`StubHttpHandler`）。
  - `samples/Certificate.Api`：示例宿主。通用审批接口目前在 `Endpoints/ApprovalEndpoints.cs`，异常映射在 `Infrastructure/ApprovalExceptionHandler.cs`，当前用户 `Infrastructure/DemoIdentityProvider.cs` 中的 `CurrentUser.Get(http)`（请求头 `X-User-Id`）。
  - `web/approval-ui`：Vue 3.5 + Element Plus 2.14 + Vue Flow 1.48 + vue-router 5，Vite 8 / vitest 5 / vue-tsc 3 / TypeScript ~5.9，pnpm；构建输出到 `samples/Certificate.Api/wwwroot`。23 个前端测试。
- 当前“不够通用”的点（本计划要解决）：
  1. 新增一种审批必须写业务表、后端服务、提交页面（无表单设计器 / 通用发起）。
  2. 通用审批接口、异常处理、当前用户获取都放在示例项目里，别的系统要复制代码。
  3. 前端：`http.ts` 写死 `X-User-Id` 演示头；`labels.ts` 写死 `businessTypeLabels`；用户 / 角色列表来自 `/api/demo/*`；证书页面与通用页面混在一个应用里。

## 1. 目标与非目标

### 目标
- **B 表单设计器 + 通用发起页**：在设计器里为流程定义表单（字段、校验、布局）与发起设置；用户在“发起申请”中心选择流程、填写表单直接发起审批，**新增审批类型无需写代码**。表单数据可用于条件分支、审批人（表单人员字段）、通知模板、动作参数；退回发起人后可修改表单重新提交。
- **C 封装**：
  - 后端新增 `src/Approval.AspNetCore`：一行 `AddApprovalAspNetCore()` + `MapApprovalEndpoints()` 提供全部通用接口、异常映射、当前用户与用户目录抽象。
  - 前端改为 pnpm workspace：`@approval/ui` 组件库（可配置的 API 地址 / 鉴权头 / 业务面板 / 路由），示例应用只保留证书业务与演示用户切换。
  - `dotnet pack` 产出 `Approval.Core`、`Approval.AspNetCore` 两个 NuGet 包；`@approval/ui` 可构建为库（dist + d.ts）。

### 非目标（本期不做，写入 README“后续扩展”）
- 附件上传字段（需要文件存储设计）。
- 节点级字段权限（审批人在某节点可编辑某些字段）——本期仅发起人在发起 / 重新提交时可编辑。
- 公式 / 计算字段（仅提供明细表数值列“合计”）。
- 草稿保存、表单版本独立于流程版本管理。
- 发布到 nuget.org / npm 公共源（只验证能打包）。

## 2. 关键设计决策（执行前若用户无异议即按此实施）

| # | 决策 | 理由 |
|---|---|---|
| D1 | 表单 schema 与发起设置**存放在流程定义 JSON 中**（`form`、`settings` 字段），随流程版本一起发布 | 实例绑定发起时的定义版本 → 表单结构天然版本一致；无需新表 |
| D2 | 通用表单审批使用固定业务类型 `Form`（可配置），**表单数据即单据**：`BusinessKey` = 新 Guid，数据存 `ApprovalInstance.FormDataJson` | 无需业务表；条件、模板已基于 FormDataJson |
| D3 | 只有 `settings.startable = true` 的流程出现在发起中心；默认 false | 证书流程由业务模块发起，不应出现在发起中心 |
| D4 | 服务端按 schema 校验并**规范化**表单数据（去掉未定义字段、类型转换、长度/数量上限），校验失败返回 400 + 字段级错误 | 不信任前端；条件分支依赖正确类型 |
| D5 | 新项目 `Approval.AspNetCore` 依赖 `Microsoft.AspNetCore.App`（FrameworkReference）；Core 不引用 ASP.NET | Core 可用于非 Web 场景（后台服务、桌面） |
| D6 | 当前用户抽象 `IApprovalCurrentUser`，默认实现读取 `ClaimsPrincipal`（`NameIdentifier` / `sub`）；示例注册基于请求头的演示实现 | 接入真实认证零代码；演示保持可切换用户 |
| D7 | 管理操作（发布流程、投递记录）权限：`ApprovalAspNetCoreOptions.AdminPolicy`（授权策略名）优先，否则 `AdminRole`（默认 `Admin`）经 `IApprovalIdentityProvider.IsInRoleAsync` 判断 | 兼容有 / 无 ASP.NET 授权体系的宿主 |
| D8 | 前端 pnpm workspace：`web/packages/approval-ui`（`@approval/ui`）+ `web/apps/demo`；库的 `exports` 开发时指向 `src/index.ts`（示例直接用源码），`publishConfig` 指向 `dist` | 示例无需先构建库；对外可发布 |
| D9 | 列表与待办显示**流程名称**（后端 join 定义表返回 `DefinitionName`），不再依赖前端 `businessTypeLabels` 映射 | 新业务无需改前端 |
| D10 | 枚举序列化为字符串：`AddApprovalAspNetCore()` 在 `HttpJsonOptions` 中添加 `JsonStringEnumConverter`（若已存在则跳过），README 说明 | 前端依赖字符串枚举 |

## 3. 数据与接口设计

### 3.1 流程定义 JSON 新增（全部可选，旧定义不受影响）

```jsonc
{
  "startNodeId": "start",
  "settings": {
    "startable": true,                 // 出现在发起中心
    "category": "人事",                 // 发起中心分组
    "description": "员工请假申请",
    "initiatorRoles": [],              // 允许发起的角色，空 = 所有人
    "titleTemplate": "{申请人}的请假申请（{form.days} 天）"   // 空 = "{流程名} - {申请人}"
  },
  "form": {
    "fields": [
      { "key": "leaveType", "label": "请假类型", "type": "Select", "required": true, "span": 12,
        "options": [{ "label": "年假", "value": "annual" }, { "label": "事假", "value": "personal" }] },
      { "key": "period", "label": "起止日期", "type": "DateRange", "required": true, "span": 12 },
      { "key": "days", "label": "天数", "type": "Number", "required": true, "min": 0.5, "max": 30, "precision": 1 },
      { "key": "handover", "label": "工作交接人", "type": "User" },
      { "key": "reason", "label": "事由", "type": "TextArea", "maxLength": 500 },
      { "key": "items", "label": "费用明细", "type": "Table", "maxRows": 50,
        "columns": [ { "key": "name", "label": "项目", "type": "Text", "required": true },
                     { "key": "amount", "label": "金额", "type": "Money", "required": true, "sum": true } ] }
    ]
  },
  "nodes": { ... }
}
```

字段类型 `FormFieldType`：`Text`、`TextArea`、`Number`、`Money`（精度 2）、`Date`、`DateRange`（`["yyyy-MM-dd","yyyy-MM-dd"]`）、`Select`、`MultiSelect`、`Radio`、`Switch`（bool）、`User`（单个用户 ID）、`Users`（用户 ID 数组）、`Table`（明细，列仅支持 Text/Number/Money/Date/Select）、`Description`（说明文字，无值）。

字段通用属性：`key`（标识符 `^[A-Za-z_][A-Za-z0-9_]*$`，唯一，用于条件 / 模板）、`label`、`type`、`required`、`placeholder`、`help`、`defaultValue`、`span`（12 / 24）、`options`、`min` / `max` / `precision`、`maxLength`、`maxRows`、`columns`。

明细合计：`Table` 中 `sum: true` 的数值列，服务端规范化时写入顶层 `{tableKey}_{colKey}_sum`（如 `items_amount_sum`），可直接用于条件 `items_amount_sum > 5000` 与模板。

### 3.2 后端新增类型（`src/Approval.Core`）

| 文件 | 内容 |
|---|---|
| `Forms/FormSchema.cs` | `FlowSettings`、`FormSchema`、`FormField`、`FormFieldOption`、`FormFieldType` |
| `Forms/FormSchemaValidator.cs` | 发布时校验 schema：key 合法唯一、选项非空、min ≤ max、表格列合法、`titleTemplate` 引用的 `form.x` 存在；`FormField` 审批人规则引用的字段须为 `User/Users` 类型（存在表单时） |
| `Forms/FormDataValidator.cs` | `Validate(FormSchema, JsonElement) → (normalizedJson, Dictionary<string,string[]> errors)`：必填、类型、选项、范围、长度、行数；去掉未定义字段；计算合计字段；上限：字段 ≤ 100、文本 ≤ 4000、表格行 ≤ maxRows（默认 100） |
| `ApprovalException.cs` 中 `ApprovalValidationException` | `ApprovalException` 子类，带 `Errors`（阶段 1 已实现，通用于所有输入校验） |
| `Forms/FormApprovalHandler.cs` | `BusinessType = "Form"` 的空回调（满足 `RequireBusinessHandler`） |
| `Engine/ApprovalFormService.cs` | `ListStartableAsync(userId)`、`GetStartFormAsync(key, userId)`、`StartAsync(key, userId, JsonElement formData, comment)`（校验 → 渲染标题 → `ApprovalEngine.StartAsync`）、`ResubmitAsync(taskId, userId, JsonElement? formData, comment)`（仅 `BusinessType == Form` 允许改表单，按实例绑定版本的 schema 校验） |
| `Abstractions/IApprovalUserDirectory.cs` | `SearchUsersAsync(keyword, take)`、`GetUsersAsync(ids)`、`ListRolesAsync()`；`ApprovalUser(Id, Name, Roles)` |
| 修改 `FlowDefinition.cs` | 增加 `Settings`、`Form` 属性 |
| 修改 `FlowDefinitionValidator.cs` | 调用 `FormSchemaValidator` |
| 修改 `ApprovalQueryService.cs` | `TodoItem` 与实例列表返回 `DefinitionName`（join `ApprovalDefinitions`） |
| 修改 `ApprovalServiceCollectionExtensions.cs` | `ApprovalBuilder.AddFormApprovals(string businessType = "Form")`、`AddUserDirectory<T>()` |
| 修改 `ApprovalException.cs` | `ApprovalErrorCode` 增加 `Unauthorized`、`Validation` |

### 3.3 `src/Approval.AspNetCore`（新项目）

```
Approval.AspNetCore.csproj   <FrameworkReference Include="Microsoft.AspNetCore.App" />，引用 Approval.Core
ApprovalAspNetCoreExtensions.cs   AddApprovalAspNetCore(o => ...)、MapApprovalEndpoints(prefix = "/api/approval") → RouteGroupBuilder
ApprovalAspNetCoreOptions.cs      AdminRole = "Admin"、AdminPolicy、EnableOutboxEndpoints = true
IApprovalCurrentUser.cs           string? GetUserId(HttpContext)；ClaimsApprovalCurrentUser（默认）
ApprovalExceptionHandler.cs       从示例迁移；新增 Unauthorized → 401、Validation → 400 ValidationProblemDetails（errors 按字段）
Endpoints/*.cs                     按领域拆分：Workbench、Instances、Tasks、Definitions、Forms、Messages、Outbox、Directory、Me
```

接口清单（前缀 `/api/approval`，全部要求当前用户；★ 为新增）：

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `me` ★ | `{ userId, roles, isAdmin }` |
| GET | `directory/users?keyword=&ids=` ★ | 用户选择器（未注册目录时返回 501 ProblemDetails） |
| GET | `directory/roles` ★ | 角色列表 |
| GET | `todo` / `done` / `started` | 工作台（含 `definitionName`） |
| GET | `instances/{id}`、`instances?businessType=&businessKey=`、`instances/{id}/my-task` | 实例 |
| POST | `instances/{id}/withdraw` | 撤回 |
| GET | `tasks/{id}/can-handle`、`tasks/{id}/return-targets` | |
| POST | `tasks/{id}/approve` / `reject`（`targetNodeId` 可选）/ `transfer` / `add-sign` | |
| POST | `tasks/{id}/resubmit` | `{ comment, formData? }`，仅表单审批可带 formData |
| GET | `meta`、`definitions`、`definitions/by-id/{id}`、`definitions/{key}`、`definitions/{key}/versions` | |
| POST | `definitions/validate`、`definitions/{key}?name=`（管理员） | |
| GET | `forms/startable` ★ | 当前用户可发起的流程（含 settings、名称，不含节点） |
| GET | `forms/{key}` ★ | 最新版本的表单 schema + settings |
| POST | `forms/{key}/start` ★ | `{ formData, comment }` → `{ instanceId }`；校验失败 400 字段错误 |
| GET/POST | `messages`、`messages/unread-count`、`messages/{id}/read`、`messages/read-all` | |
| GET/POST | `outbox`、`outbox/{id}/retry`（管理员） | |

示例项目改动：删除 `Endpoints/ApprovalEndpoints.cs`、`Infrastructure/ApprovalExceptionHandler.cs`；新增 `Infrastructure/DemoCurrentUser.cs`（请求头实现 `IApprovalCurrentUser`）；`DemoIdentityProvider` 实现 `IApprovalUserDirectory`；`CertificateEndpoints` 改用 `IApprovalCurrentUser`；`Program.cs` 改为 `AddApprovalAspNetCore()` + `.AddFormApprovals()` + `app.MapApprovalEndpoints()`；保留 `/api/demo/users`（用户切换器）与 Webhook 演示接收端。

### 3.4 前端 workspace 结构

```
web/
├─ package.json            根：scripts { dev, build, typecheck, test } 转发到各包（pnpm -r）
├─ pnpm-workspace.yaml     packages: ['packages/*', 'apps/*']
├─ packages/approval-ui/   @approval/ui
│  ├─ package.json         exports "." → ./src/index.ts；publishConfig.exports → ./dist；peerDeps vue / vue-router / element-plus / @vue-flow/*
│  ├─ vite.config.ts       库模式（build.lib），external peer 依赖；vitest
│  └─ src/
│     ├─ index.ts          导出 plugin、createApprovalRoutes、组件、api、类型
│     ├─ config.ts         ApprovalUIOptions { apiBase='/api/approval', headers?: () => Record<string,string>, businessPanels?, onUnauthorized? }；provide/inject
│     ├─ api/              http（读取 config）、approval、types
│     ├─ composables/      useAsyncData、useApprovalUser（GET /me）、useDirectory（用户 / 角色缓存）
│     ├─ components/       MessageBell、UserPicker（基于 directory）
│     ├─ features/         flow、designer、approval、workbench、definitions、outbox、forms（新增）、start（新增）
│     └─ views/            WorkbenchView、ApprovalDetailView、DefinitionsView、DesignerView、OutboxView、StartCenterView、StartFormView
└─ apps/demo/              示例宿主（原 web/approval-ui 剩余部分）
   ├─ src/main.ts          app.use(ApprovalUI, { headers: () => ({ 'X-User-Id': currentUserId.value }), businessPanels: { Certificate: CertificateRequestPanel } })
   ├─ src/router.ts        children: [...createApprovalRoutes(), certificates]
   ├─ src/layouts/AppLayout.vue、components/UserSwitcher.vue、stores/session.ts
   ├─ src/features/certificates/、views/CertificatesView.vue、api/certificates.ts
   └─ vite.config.ts       outDir ../../../samples/Certificate.Api/wwwroot；alias '@' → src
```

`createApprovalRoutes()` 路由名统一加前缀 `approval-`（如 `approval-detail`），路径：`workbench`、`approvals/:id`、`start`、`start/:key`、`definitions`、`designer/:key`、`outbox`。

### 3.5 前端表单功能（`@approval/ui/features/forms`）

| 组件 | 说明 |
|---|---|
| `formModel.ts` | 类型、字段默认值、`newFieldKey`、`buildRules(schema)`（与服务端规则一致）、`fieldsForCondition(schema)`（含合计字段）、单元测试 |
| `DynamicForm.vue` | `schema` + `v-model` + `readonly`；el-form 栅格（span）；`expose validate()`；`serverErrors` 显示服务端字段错误；Table 字段行增删 + 合计显示；User/Users 使用 `UserPicker` |
| `FormDesigner.vue` | 左：字段类型面板（点击或拖入）；中：字段列表（原生 HTML5 拖拽排序、选中高亮、复制、删除）；右：`FieldInspector`；顶部“预览”切换为 `DynamicForm` |
| `FieldInspector.vue` | 标签、key（校验合法唯一）、必填、占位、帮助、默认值、宽度、选项编辑器、数值范围 / 精度、长度、表格列编辑器（复用简化版） |
| `FlowSettingsPanel.vue` | 可发起、分类、描述、发起角色、标题模板（变量标签插入，含表单字段） |

设计器页面改为三个页签：**流程**（现有画布）/ **表单** / **设置**。`useFlowDesigner` 增加 `form`、`settings` 状态，纳入快照（撤销 / 重做、未发布标记）与 `toDefinition`。
设计器联动：审批人规则 `FormField` 改为下拉选择表单中的 User/Users 字段；条件分支面板显示可用字段（含合计字段）点击插入；通知模板变量增加 `form.<key>` 标签。

发起中心：`StartCenterView`（按分类分组的流程卡片，搜索）→ `StartFormView`（表单 + 意见 → 提交 → 跳转审批详情；服务端字段错误回显到表单）。
审批详情：`BusinessType == 'Form'` 时显示只读 `DynamicForm`（schema 取实例绑定版本的定义）；被退回发起人时“重新提交”对话框内嵌可编辑表单（预填当前数据）。

## 4. 实施清单

### 阶段 0：准备
- [x] 询问用户：是否先提交当前未提交的改动（并行 / 退回 / 加签）。
- [x] 基线验证：`dotnet test -c Release`（76）、`cd web/approval-ui; pnpm typecheck; pnpm test`（23）。

### 阶段 1：后端封装 `Approval.AspNetCore`（C-后端）
- [x] 新建项目并加入 `Workflow.slnx`；`ApprovalErrorCode` 增加 `Unauthorized`、`Validation`。
- [x] `IApprovalCurrentUser` + `ClaimsApprovalCurrentUser`；`IApprovalUserDirectory`（放 Core.Abstractions）+ `ApprovalBuilder.AddUserDirectory<T>()`。
- [x] 迁移异常处理器（新增 401 / 字段校验 400）；`AddApprovalAspNetCore()` 注册 ProblemDetails、异常处理器、枚举字符串转换、选项。
- [x] 迁移并按领域拆分全部端点；管理员校验（策略优先、角色兜底）；新增 `me`、`directory/*`。
- [x] `ApprovalQueryService` 返回 `DefinitionName`（待办 `TodoItem`；已办 / 我发起的改为 `InstanceListItem`）。
- [x] 示例改造：`DemoCurrentUser`、`DemoIdentityProvider : IApprovalUserDirectory`、证书端点改用 `IApprovalCurrentUser`、`Program.cs` 精简。
- [x] 新建 `tests/Approval.AspNetCore.Tests`（`Microsoft.AspNetCore.TestHost`，测试内用 `WebApplication.CreateBuilder` + `UseTestServer` 构建最小宿主，不依赖示例）：未登录 401、`me`、非管理员发布 403、枚举为字符串、ProblemDetails `code`、`directory`。
- [x] 验证：`dotnet build -c Release`、全部测试、`smoke-test.ps1` / `output/e2e/e2e-v3.ps1` 回归。

### 阶段 2：表单后端（B-后端）
- [x] `Forms/*` 模型与 `FlowDefinition.Settings/Form`。
- [x] `FormSchemaValidator`（接入发布校验）、`FormDataValidator`（规范化 + 合计）。
- [x] `ApprovalFormService` + `AddFormApprovals()`（注册服务与 `FormApprovalHandler`）。
- [x] 端点：`forms/startable`、`forms/{key}`、`forms/{key}/start`；`tasks/{id}/resubmit` 支持 formData。
- [x] 标题模板变量 `{流程名}`、`{申请人}`、`{发起时间}`、`{form.x}`（仅标题模板可用，由 `ApprovalFormService` 渲染时提供，不进入通知模板目录）。
- [x] 示例流程：`Flows/leave.json`（请假：类型、起止、天数、交接人、事由；直属领导 →（days > 3）部门经理 → 人事抄送），`Flows/expense.json`（报销：明细表 + 合计；`items_amount_sum > 5000` 走 并行[财务, 负责人]，否则 财务）。`appsettings.json` 演示用户增加 `hana`（HR）、`fiona`（Finance）。
- [x] 测试（Core）：schema 校验各错误、数据校验（必填 / 类型 / 选项 / 范围 / 未知字段剔除 / 表格行与合计）、发起人角色限制、标题模板、条件按合计分支、退回后带新表单重新提交、非表单业务不能改表单。
- [x] 测试（AspNetCore）：发起 400 字段错误结构、发起成功返回 instanceId、startable 过滤（角色 / startable=false）。

- 补充：`/me` 返回 `formsEnabled`；未调用 `AddFormApprovals()` 时不映射表单接口；`Flows/*.json` 顶层 `name` 作为演示流程名称；e2e 脚本 `output/e2e/e2e-forms.ps1`。

### 阶段 3：前端 workspace 重构（C-前端）
- [x] 建立 `web/package.json`、`pnpm-workspace.yaml`；用 `git mv` 迁移文件（保留历史），通用代码 → `packages/approval-ui/src`，证书 / 布局 / 演示用户 → `apps/demo/src`。
- [x] `config.ts`（provide/inject + 模块级默认值供非组件代码使用）；`http.ts` 改为读取配置的 `apiBase`、`headers()`、`onUnauthorized`。
- [x] `useApprovalUser`（`/me`）替换 `useCurrentUser` 中的通用部分；用户 / 角色列表改用 `directory` 接口；`UserPicker` 组件。
- [x] 列表显示 `definitionName`；删除 `businessTypeLabels`；业务面板改由配置注入。
- [x] `createApprovalRoutes()`、`ApprovalUI` 插件、`index.ts` 导出；示例应用接入。
- [x] 库构建：`vite build`（lib 模式，external peer）+ `vue-tsc -p tsconfig.lib.json --declaration --emitDeclarationOnly`；`pnpm --filter @approval/ui build` 产出 `dist`。
- [x] 迁移现有测试（`flowModel.test.ts`、`history.test.ts`）到库包；示例构建输出仍到 `wwwroot`。
- [x] 验证：`cd web; pnpm install; pnpm -r typecheck; pnpm -r test; pnpm build`；浏览器回归证书流程、设计器、投递记录。

- 补充：库内部改用相对路径导入（宿主的 `@` 别名指向宿主 src）；配置为模块级（`configureApprovalUI`），`userKey` 驱动切换用户时刷新；演示 Webhook 面板经 `outboxPanel` 选项注入；`UserPicker` 基于用户目录远程搜索。

### 阶段 4：表单前端（B-前端）
- [x] `formModel.ts` + 测试（规则生成、key 生成、合计字段、默认值）。
- [x] `DynamicForm.vue`、`UserPicker` 接入、Table 字段。
- [x] `FormDesigner.vue`、`FieldInspector.vue`、`FlowSettingsPanel.vue`；设计器三页签；`useFlowDesigner` 纳入 form / settings（快照、撤销、dirty）。
- [x] 设计器联动：FormField 审批人下拉、条件可用字段、通知模板 form 变量。
- [x] 发起中心与发起页（路由、菜单项“发起申请”）；服务端字段错误回显。
- [x] 审批详情：表单只读展示（注册为内置业务面板 `Form`）；重新提交可编辑表单。
- [x] 验证：typecheck / test / build。

- 补充：`/me` 增加 `formBusinessType`（前端据此判断重新提交时能否改表单）；设计器通过 provide/inject（`designerContext.ts`）向审批人、条件、通知面板共享表单结构；示例 `/start/{**key}` 路由回退（流程标识含点）。

### 阶段 5：联调、文档、打包
- [ ] 浏览器端到端（admin 新建流程 → 设计表单 → 设置可发起 → 发布；alice 在发起中心提交请假 3 天与 5 天分别走不同分支；退回 alice 修改天数重新提交；报销明细合计 > 5000 走并行；证书业务回归；控制台 0 错误）。
- [ ] `dotnet pack src/Approval.Core src/Approval.AspNetCore -c Release -o output/packages` 成功；`pnpm --filter @approval/ui build` 成功。
- [ ] README：新增“接入你的系统”（后端 3 步：AddApprovalWorkflow + 身份 / 目录 / 当前用户 + MapApprovalEndpoints；前端：安装 `@approval/ui`、插件配置、挂载路由）、“表单审批”（字段类型、合计字段、条件写法）；更新目录结构与运行命令（`cd web; pnpm install; pnpm build`）。
- [ ] 全量检查：乱码扫描、`git diff --check`、所有测试与构建；向用户报告（中文，按 改了什么 / 如何验证 / 注意事项 / 下一步）。

## 5. 验证命令汇总

```powershell
cd D:\Study\CSharp\Workflow
dotnet test -c Release                              # Core + AspNetCore 测试
cd web; pnpm install; pnpm -r typecheck; pnpm -r test; pnpm build
cd ..; dotnet pack src/Approval.Core src/Approval.AspNetCore -c Release -o output/packages
# 运行（新库）：
Remove-Item output/e2e/v4.db* -ErrorAction SilentlyContinue
$env:ConnectionStrings__Default='Data Source=D:\Study\CSharp\Workflow\output\e2e\v4.db'
dotnet run --project samples/Certificate.Api -c Release --no-launch-profile --urls http://localhost:5199
```

## 6. 执行环境注意事项（踩过的坑）

- 回复与代码注释一律中文；编辑后用 Grep 搜索 U+FFFD 替换字符检查乱码（生成中文文本时偶发），发现立即修正。
- Visual Studio 可能占用 Debug 输出：构建 / 测试一律 `-c Release`；示例运行在 5199 端口，`--no-launch-profile`，使用 `output/e2e/*.db` 独立数据库（表结构变化后删除重建）。
- 本机有系统代理：PowerShell 访问 localhost 用 `Invoke-RestMethod -NoProxy`；示例已用 `LoopbackBypassProxy` 让 HttpClient 访问本机不走代理。
- PowerShell 中 `R` 是内置别名（Invoke-History），不要用作函数名；多行替换遇到 CRLF 文件会匹配失败——用 Edit 工具或 `output/*.py` 补丁脚本（python 可用）。
- Bash heredoc 写入大段 Vue 代码容易因引号失败，用 Write 工具写文件。
- playwright-cli：截图只能保存到浏览器打开时所在目录下（在 `web/...` 下打开则存到该目录的 `.playwright-cli/`）；设计器有未保存修改时 `goto` 会被离开确认拦截，需 `playwright-cli close` 后重新 `open`；隐藏的 radio 需点击 label。
- `defineModel` 的值要等父组件回传，同一事件里连续两次赋值会覆盖——一次性构造完整新值。
- 内存紧张时后台服务可能被系统终止，验证前确认 5199 在监听。

## 7. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 前端目录大迁移导致导入路径大量失效 | `git mv` 保留历史；先迁移再逐个修复 `vue-tsc` 报错；库与示例都配置 `@` 别名各自指向自己的 `src`，库内部不使用跨包相对路径 |
| 全局 JSON 枚举转换影响宿主其他接口 | 仅在未配置时添加；README 说明；可通过选项关闭 |
| 表单数据未经校验进入条件分支导致求值异常 | 服务端规范化 + 类型转换；条件求值异常已映射为 InvalidDefinition 并回滚 |
| 用户提交超大表单 | 字段数、文本长度、表格行数上限；Kestrel 默认请求体限制 |
| 发起中心暴露不应发起的流程 | `startable` 默认 false；`initiatorRoles` 服务端校验 |
| 证书示例回归 | 阶段 1、3、5 都运行 `e2e-v3.ps1` 与浏览器回归 |

## 8. 回滚

各阶段独立（建议每阶段结束后经用户同意提交一次）。表单相关字段在定义 JSON 中可选，回滚代码后旧定义与数据仍可用；本计划不新增数据库表 / 列（`DefinitionName` 为查询时 join）。
