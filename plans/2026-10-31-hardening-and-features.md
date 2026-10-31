# 优化计划：数据权限 / 分页 / 组织架构审批人 / 表单增强 / 管理功能

> 状态：**执行中**（2026-10-31 开始；阶段 0–4 已完成）。
> 对应上次评估中的第 1、2、6、7、8 项。执行时按“阶段 → 清单”逐项完成，每阶段结束运行验证命令，并更新本文件复选框。

## 0. 背景与现状（执行前必读）

- 仓库 `D:\Study\CSharp\Workflow`，远程 `https://github.com/ttcc666/WorkflowDemo.git`，分支 `main`。
- 本地领先远程 6 个提交（`1df680d` … `328b75a`），**均未推送**。
- **另有一处未提交的修复**：动作失败时撤销做了一半的数据修改。
  - 涉及 `ChangeTrackerCheckpoint.cs`（新文件）、`ApprovalOutboxService.cs`、`ApprovalEngine.Actions.cs`、`ActionRollbackTests.cs`（新文件）、`TestInfrastructure.cs`、`README.md`。
  - 阶段 0 先提交（需用户确认）。
- 用户约定：
  - 回复一律中文，最终回复按“改了什么 / 如何验证 / 注意事项 / 下一步”。
  - 每阶段完成后**本地提交、不推送**（上次的选择，开始时再确认一次）。
  - 推送须用户明确同意。
- 结构：
  - `src/Approval.Core`：引擎。
  - `src/Approval.AspNetCore`：接口，按领域拆在 `Endpoints/*.cs`，权限辅助在 `ApprovalHttpContextExtensions`。
  - `samples/Certificate.Api`：示例宿主。
  - `tests/Approval.Core.Tests`（96 个测试）。
  - `tests/Approval.AspNetCore.Tests`（14 个测试，TestServer，`ApiHost`）。
  - 前端 `web/packages/approval-ui`（`@approval/ui`，32 个测试）、`web/apps/demo`。
- 端到端脚本（不提交，`output/` 已忽略）：
  - `samples/Certificate.Api/smoke-test.ps1`
  - `output/e2e/e2e-v3.ps1`
  - `output/e2e/e2e-forms.ps1`
  - `output/e2e/e2e-outbox-retry.ps1`
  - 浏览器辅助函数 `output/e2e/pw.ps1`：`snap`、`ref`、`find`、`asUser`、`js`、`pick`。

## 1. 目标与非目标

### 目标
| # | 内容 |
|---|---|
| 1 | **数据权限**：审批详情、按业务查询、流程定义详情、附件只对有关的人可见；宿主可自定义规则 |
| 2 | **分页与筛选**：待办 / 已办 / 我发起的 / 抄送我的 / 管理员监控 / 投递记录支持分页、关键字、状态、流程筛选 |
| 6 | **组织架构审批人**：部门负责人、第 N 级上级、逐级审批到第 N 级（连续多级）；设计器按类型自动生成参数控件 |
| 7 | **表单增强**：草稿、计算字段（含明细列公式）、节点字段权限（审批人可编辑 / 隐藏字段）、附件字段 |
| 8 | **管理功能**：终止流程、管理员改派待办、离职交接（批量改派）、流程监控页、统计 |

### 非目标（写入 README“后续扩展”）
- 跳转到任意节点、流程迁移到新版本；会签比例通过、条件并行。
- 附件在线预览、病毒扫描、对象存储（只提供本地磁盘实现 + 存储接口）。
- 统计图表库（只做表格和简单条形）；跨库 SQL 聚合优化。
- 字段级加密、审计日志导出。

## 2. 关键设计决策

| # | 决策 | 理由 |
|---|---|---|
| D1 | 新增 `IApprovalAccessPolicy`（Core），默认实现 `DefaultApprovalAccessPolicy`；宿主可注册替换或继承 | 不同组织的可见范围差异大，必须可扩展 |
| D2 | 默认可见：发起人；任意任务的处理人或候选人（用户任务本人、角色任务的角色成员、实际处理人）；抄送对象（含角色成员）；管理员 | 与常见 OA 一致；角色候选人需要先看内容才能审批 |
| D3 | 不可见时返回 **404**（`NotFound`）而不是 403 | 不暴露实例是否存在 |
| D4 | 抄送对象落库：新表 `ApprovalCarbonCopies`（InstanceId、NodeId、AssigneeKind、AssigneeValue、CreatedAt、Round），角色抄送在查看时按角色判断 | 用于权限判断和“抄送我的”列表；现在抄送只写在轨迹文本里，无法查询 |
| D5 | 流程定义完整 JSON（含 HTTP 动作参数、通知配置，可能有密钥）**仅管理员**可读；实例查看者通过 `instances/{id}/definition` 获取**公开视图**（去掉动作参数、通知规则、超时策略等，只留节点、连线、名称、类型、坐标、表单结构） | 现在任何登录用户都能读到 HTTP 动作里的请求头 / 令牌 |
| D6 | 分页统一为 `PagedResult<T>(Items, Total, Page, PageSize)`；参数 `page`（从 1 开始）、`pageSize`（默认 20，最大 100）、`keyword`、`status`、`definitionKey`；**直接修改现有接口的返回结构**（未发布，README 注明） | 保持接口简洁；前端同步修改 |
| D7 | 组织架构抽象 `IApprovalOrganizationProvider`：`GetDepartmentAsync(userId)`、`GetDepartmentLeadersAsync(deptId)`、`GetParentDepartmentAsync(deptId)`、`GetManagerAsync(userId)`；内置解析器基于它实现 | 宿主对接 HR / 钉钉 / 企业微信组织架构只需实现一个接口 |
| D8 | 审批人类型描述 `ApproverTypeDescriptor(Type, DisplayName, ValueKind, Help, Options?)`，`ValueKind`：None / User / Role / FormUserField / Level / Text；`meta.approverTypes` 改为描述对象数组 | 设计器按描述自动生成参数控件，自定义解析器也能有好的编辑体验 |
| D9 | 组织架构类审批人默认以**发起人**为基准，值可写 `2@payee`，表示以表单人员字段 `payee` 为基准 | 覆盖“收款人的上级”等常见需求，格式简单 |
| D10 | 逐级审批 `ManagerChain`：解析为从 1 级到 N 级的上级列表，节点按**依次审批**生成任务（解析器返回有序列表，节点 `Mode` 强制 Sequential，设计器提示）；遇到没有上级时截止；去重（同一人只审一次） | 最常见的“逐级审批到总监”场景 |
| D11 | 计算字段 `Formula`：自研**四则运算小语法**（数字、字段、`+ - * / ()`、`round(x,n)`、`min`、`max`、`abs`、`if(cond, a, b)` 与比较运算），C# 与 TS 各实现一份，**共用一份 JSON 测试用例**（`tests/shared/formula-cases.json`） | 前端实时预览与服务端结果必须一致；不复用 DynamicExpresso，避免表达式能力过大 |
| D12 | 计算值以服务端为准：规范化时按依赖顺序重新计算（忽略前端提交的计算字段值）；发布时检查引用存在与循环依赖 | 防篡改 |
| D13 | 明细列公式：`Table` 的列可为 `Formula`（如 `qty * price`），按行计算；可参与合计 | 报销、采购常用 |
| D14 | 节点字段权限 `FlowNode.FieldPermissions: Dictionary<fieldKey, "Editable" \| "Hidden">`（未列出 = 只读），**仅表单审批**生效 | 业务审批的数据以业务单据为准，不允许在审批中修改 |
| D15 | 审批人同意时可提交 `formData`（仅包含本节点可编辑字段，其他字段被拒绝）；合并后整表重新校验与计算；轨迹记录修改了哪些字段，旧值 / 新值存入 `ApprovalLogs.Details`（新增可空列，JSON） | 审计可追溯；轨迹文本不含值，避免泄露隐藏字段 |
| D16 | 隐藏字段由服务端过滤：查看者若**只**以某些节点的处理人 / 抄送人身份可见，且字段在这些节点**都**设为 Hidden，则详情接口的 `formDataJson` 与日志 `Details` 去掉该字段；发起人、管理员看全部 | 隐藏必须服务端执行，前端隐藏不安全 |
| D17 | 附件：字段类型 `File`（值为 `[{id,name,size,contentType}]`），存储接口 `IApprovalFileStore`，默认 `LocalApprovalFileStore`（配置目录，随机文件名，不放在 wwwroot）；元数据表 `ApprovalFiles`（Id、FileName、ContentType、Size、Sha256、UploadedBy、UploadedAt、InstanceId?、DraftId?） | 存储可替换（OSS / S3）；元数据用于权限判断与清理 |
| D18 | 附件权限：未绑定时仅上传者可访问；绑定实例后按实例可见性（D1）；下载一律 `Content-Disposition: attachment` 加 `X-Content-Type-Options: nosniff`；扩展名白名单、单文件大小、字段文件数上限 | 防越权下载、防存储型 XSS |
| D19 | 未绑定文件清理：后台任务删除 `InstanceId` 与 `DraftId` 都为空且超过 24 小时的文件（并入超时扫描或新的后台任务） | 防磁盘被占满 |
| D20 | 草稿：新表 `ApprovalDrafts`（Id、UserId、DefinitionKey、FormDataJson、Comment、CreatedAt、UpdatedAt）。保存时**不做必填校验**，只做结构规范化（剔除未定义字段、大小上限 256KB）；每人最多 50 份；提交时同一事务删除草稿；附件随草稿绑定 / 解绑 | 草稿常常未填完 |
| D21 | 终止：`TerminateAsync(instanceId, operatorId, reason)`，状态 `Terminated`；业务回调新增默认接口方法 `OnTerminatedAsync => OnRejectedAsync`（兼容旧实现）；新增 `ApprovalAction.Terminate`；触发 `Completed` 通知 | 现有业务（证书）终止时按驳回处理（关闭申请、解锁） |
| D22 | 改派：`ReassignAsync(taskId, operatorId, toUserId, reason)`，复用转交逻辑但**跳过“当前处理人”校验**；角色任务可改派给具体用户；新增 `ApprovalAction.Reassign`；批量 `ReassignUserAsync(fromUserId, toUserId, operatorId)` 改派某人的全部待办（不含角色任务） | 离职交接、处理人长期不在 |
| D23 | 管理接口统一在 `/api/approval/admin/*`，用 `EnsureApprovalAdminAsync`；Core 方法不判断管理员（权限在接口层），但记录操作人 | 与现有发布流程的权限方式一致 |
| D24 | 统计：取时间范围内（最大 366 天、最多 10 万条）的实例与任务必要列，在内存中聚合（各流程数量与状态分布、平均 / P90 耗时、各节点平均处理时长、超时数、待办积压前 10 的处理人）；不依赖数据库日期函数 | 兼容 SQLite / SQL Server / PostgreSQL；数据量大的部署可替换为宿主自己的报表 |
| D25 | 数据库变更：新表 `ApprovalCarbonCopies`、`ApprovalFiles`、`ApprovalDrafts`；新列 `ApprovalLogs.Details`；新索引（Tasks(HandledBy)、Tasks(AssigneeKind, AssigneeValue, Status)、Instances(InitiatorId, CreatedAt)、Instances(Status, CreatedAt)、CarbonCopies(AssigneeKind, AssigneeValue)）。示例的 `EnsureSchemaUpToDateAsync` 增加检查，旧演示库需删除重建；README 给出“正式项目生成 EF 迁移”的说明 | 全部为新增，不改已有列 |

## 3. 详细设计

### 3.1 数据权限（第 1 项）

**Core**
- `Abstractions/IApprovalAccessPolicy.cs`：
  ```csharp
  public interface IApprovalAccessPolicy
  {
      /// <summary>返回查看级别：None / Participant（受字段隐藏规则约束）/ Full（发起人、管理员、宿主授权）。</summary>
      Task<ApprovalAccess> GetAccessAsync(ApprovalViewer viewer, ApprovalInstance instance, CancellationToken ct);
  }
  public sealed record ApprovalViewer(string UserId, bool IsAdmin);
  public sealed record ApprovalAccess(ApprovalAccessLevel Level, IReadOnlySet<string> VisibleViaNodes);
  ```
- `DefaultApprovalAccessPolicy`：按 D2 判断；`VisibleViaNodes` 为该用户作为处理人或抄送人涉及的节点（用于 D16）；方法为 `virtual`，宿主可继承后追加规则（如“证书管理员可看全部证书审批”）。
- `Domain/ApprovalCarbonCopy` 实体及表配置；`ApprovalEngine` 的 `NodeType.Cc` 分支写入抄送记录（每个接收人一行，记录 Round）。
- `ApprovalQueryService`：
  - `GetInstanceForViewerAsync(id, viewer)`：权限判断 + 隐藏字段过滤，返回 `InstanceDetail`（实例、任务、轨迹、`access`、`hiddenFields`）。
  - `GetByBusinessForViewerAsync(...)`：逐条过滤。
  - 原 `GetInstanceAsync` 保留，供宿主后台使用，XML 注释写明“不做权限判断”。
- `Definitions/FlowDefinition.ToPublicView()`：去掉 `actions[].parameters`、`notifications`、`timeout`、`settings.initiatorRoles`，保留表单与节点结构。

**AspNetCore**
- `instances/{id}`：调用 `GetInstanceForViewerAsync`，无权限返回 404。
- `instances?businessType&businessKey`：过滤后返回。
- 新增 `instances/{id}/definition`（公开视图，需实例可见）。
- `definitions`、`definitions/{key}`、`definitions/by-id/{id}`、`definitions/{key}/versions`：仅管理员（`EnsureApprovalAdminAsync`）。
  - 若 `meta` 也有敏感项，同样处理；`meta` 目前只有类型与渠道名，保持对所有人开放。
- `ApprovalAspNetCoreOptions.DefinitionReaders`：可选角色列表，允许非管理员（如流程设计员）读完整定义，默认空。
- `ApprovalHttpContextExtensions.GetApprovalViewerAsync()`：组装 `ApprovalViewer`。

**示例宿主**
- `/api/certificate-requests/{id}` 返回的 `approval` 改为调用 `GetByBusinessForViewerAsync`（或在无权时返回 null）。
- 证书列表本身属于业务数据，保持原样（演示），README 说明业务接口的权限由宿主负责。

**前端**
- 详情页改用 `approvalApi.instanceDefinition(id)`。
- 404 显示“无权查看或不存在”。
- 设计器、流程定义菜单对非管理员隐藏（`useApprovalUser().isAdmin`）。
- `DefinitionsView` 对非管理员显示提示。

### 3.2 分页与筛选（第 2 项）

- Core 新增 `Engine/Paging.cs`：`PageRequest(Page, PageSize)`（Clamp）、`PagedResult<T>`、扩展方法 `ToPagedAsync(this IQueryable<T>, PageRequest, ct)`（先 Count 再 Skip / Take，保证排序稳定：CreatedAt + Id）。
- `ApprovalQueryService`：
  - `GetTodoAsync(userId, TodoQuery)`：keyword（标题 / 申请人）、definitionKey、overdueOnly。
  - `GetDoneAsync(userId, InstanceQuery)`、`GetStartedByAsync(userId, InstanceQuery)`：status、definitionKey、keyword、from / to。
  - **新增** `GetCarbonCopiedAsync(userId, InstanceQuery)`：抄送我的，用户抄送或所在角色的抄送。
  - **新增** `SearchInstancesAsync(InstanceQuery)`（管理员监控，含 initiatorId 筛选）。
  - `GetTodoCountAsync(userId)`：工作台角标只取数量。
- 接口：
  - `todo`、`done`、`started`、`cc`、`admin/instances` 接受 `page`、`pageSize`、`keyword`、`status`、`definitionKey`、`from`、`to`。
  - `outbox` 增加分页。
  - `todo/count` 新增。
- 索引见 D25（`ApprovalModelBuilderExtensions`）。
- 前端：
  - `TodoTable` / `InstanceTable` 增加 `el-pagination` 与筛选栏；页码、筛选同步到路由 query。
  - 工作台新增“抄送我的”页签；待办角标用 `todo/count`。
  - `OutboxView` 分页。
  - 新增 `composables/usePagedList.ts`（封装 page / pageSize / 筛选 / 重载 / 路由同步）。

### 3.3 组织架构审批人（第 6 项）

**Core**
- `Abstractions/IApprovalOrganizationProvider.cs`（D7），`ApprovalDepartment(Id, Name, ParentId)`。
- `Resolvers/OrganizationResolvers.cs`：
  - `DeptLeader`：值 `N[@field]`，N=1 为本部门负责人，N=2 为上级部门负责人……没有上级部门时返回空（走节点的 `EmptyApproverPolicy`）。
  - `Manager`：第 N 级直属上级（沿 `GetManagerAsync` 向上 N 次）。
  - `ManagerChain`：1 到 N 级上级的有序列表（去重，遇到发起人自己则跳过）。
  - 解析时的基准用户：`@field` 时取表单人员字段的值（User 类型；Users 类型取第一个，发布时提示）。
- `ApprovalBuilder.AddOrganizationProvider<T>()`：同时注册 3 个解析器；未注册组织架构接口时不注册这些类型，设计器里也就不出现。
- 审批人类型描述（D8）：
  - `IApproverResolver` 增加默认接口属性 `Descriptor`（默认 `new(Type, Type, ValueKind.Text)`，兼容现有自定义解析器）。
  - 内置解析器补全描述：User→User、Role→Role、FormField→FormUserField、Initiator→None、DeptLeader/Manager/ManagerChain→Level（支持 `@field`）。
  - `ApprovalMetadata.ApproverTypes` 改为 `IReadOnlyList<ApproverTypeDescriptor>`；`RecipientTypes` 同样改为描述（Assignees、Approvers 为 None）。
- `ManagerChain` 与审批方式：引擎在审批节点解析时，若规则来自“有序解析器”（`ApproverTypeDescriptor.Ordered = true`），按返回顺序生成依次审批任务；节点 `Mode` 不是 Sequential 时**发布校验报错**（提示改为依次审批）。
- 发布校验：Level 值必须是 1–10 的整数，可带 `@字段`，字段必须是表单人员字段（存在表单时）。

**示例**
- `appsettings.json` 增加 `Departments`（Id、Name、ParentId、Leaders）；`DemoUsers` 增加 `Department`、`Manager`。
- `DemoIdentityProvider` 实现 `IApprovalOrganizationProvider`。
- `leave.json` 改为“直属上级（Manager 1）→ 天数 > 3 时部门负责人（DeptLeader 1）”。
- 新增 `purchase.json`（采购：逐级审批到 2 级上级，`ManagerChain 2`，金额 > 10000 再加财务）。
- 示例组织（建议）：
  - 总经办（leo 负责）
  - 研发部（dave 负责，上级总经办）
  - 质量部（quinn 负责，上级总经办）
  - 人事部（hana）、财务部（fiona）
  - alice、bob 属研发部，上级 dave；dave 的上级 leo。

**前端**
- `ApproverRulesEditor` 改为按 `ValueKind` 渲染：Level 显示“级数”数字输入加“基准人员”下拉（发起人 / 表单人员字段）；未知类型仍用文本框。
- `approverSummary` 显示“部门负责人（上 1 级）”“第 2 级上级”“逐级审批至第 2 级上级”。
- 类型、文案从 `meta.approverTypes` 的描述读取，删除前端写死的 `approverTypeLabels`（保留兜底）。

### 3.4 表单增强（第 7 项）

**7a 草稿**
- 实体 `ApprovalDraft`、表配置。
- `ApprovalFormService`：
  - `SaveDraftAsync(userId, key, draftId?, formData, comment)`：宽松规范化，复用 `FormDataValidator.Normalize`，增加 `lenient` 参数——跳过必填，类型不对的值丢弃。
  - `ListDraftsAsync`、`GetDraftAsync`、`DeleteDraftAsync`。
  - `StartAsync` 增加可选 `draftId`：提交成功同一事务删除草稿。
- 接口：`GET forms/drafts`、`GET forms/drafts/{id}`、`PUT forms/{key}/draft`（body：draftId?、formData、comment，返回 draftId）、`DELETE forms/drafts/{id}`。
- 流程版本变化：打开草稿时用最新表单，按 key 保留可用的值，前端提示“流程已更新，部分字段已调整”。
- 前端：
  - 发起页增加“保存草稿”按钮，以及 30 秒自动保存（有修改时）。
  - 离开页面有未保存修改时提示。
  - 发起中心顶部“我的草稿”列表（继续编辑 / 删除）。
  - 路由 `start/:key?draft=<id>`。

**7b 计算字段**
- Core `Forms/FormulaEngine.cs`：词法 + 递归下降解析为 AST（数字、标识符、二元运算、比较、函数调用），按 decimal 计算；除以 0 返回 null；空值参与运算返回 null。
- `FormFieldType.Formula`，属性 `formula`、`precision`；`Table` 列也可为 Formula（引用同行列与顶层字段）。
- `FormSchemaValidator`：
  - 公式语法正确；引用的字段存在且为数值类（Number / Money / Formula / 合计字段）。
  - 循环依赖检测（拓扑排序）。
  - 明细列公式只能引用本行数值列。
- `FormDataValidator`：规范化最后一步按拓扑顺序计算全部公式字段（行内公式 → 合计 → 顶层公式），覆盖提交的值。
- 前端：
  - `features/forms/formula.ts`：同语法实现。
  - `DynamicForm` 中公式字段只读、实时计算显示。
  - `FieldInspector` 提供公式输入，可点击插入字段，并实时显示语法错误。
- 共享测试用例：`tests/shared/formula-cases.json`（表达式、变量、期望结果 / 错误）。xunit 用 `[MemberData]` 读取；vitest 用 `import cases from '../../../../../tests/shared/formula-cases.json'`（需要时配置 vite `server.fs.allow`）。

**7c 节点字段权限**
- `FlowNode.FieldPermissions`（D14）；发布校验：字段存在、只在审批节点上、只有表单审批才能设置可编辑（流程定义层面无法得知业务类型，改为**运行时**校验：非表单业务类型提交 formData 时拒绝）。
- 引擎：
  - `ApproveAsync(taskId, userId, comment, formData?)`。
  - 新增 `ApprovalFormService.ApproveWithFormAsync`（校验可编辑字段 → 合并 → 全表校验 → 计算 → 写入 FormDataJson → 记录 `Details` 差异 → 调用引擎同意），与 `ResubmitAsync` 同样的模式。
  - 标题模板若引用被修改字段，同步更新标题。
- `ApprovalLog.Details`（string?，JSON：`[{field,label,before,after}]`）。
- 读取：
  - `GetInstanceForViewerAsync` 根据 `VisibleViaNodes` 与各节点 `FieldPermissions` 计算 `hiddenFields`，从 `formDataJson` 与日志 `Details` 中剔除。
  - 返回 `editableFields`：当前用户待办所在节点可编辑的字段。
- 接口：`tasks/{id}/approve` 请求体增加 `formData`。
- 前端：
  - 设计器审批节点属性增加“字段权限”页签（列出表单字段：只读 / 可编辑 / 隐藏）。
  - 详情页对可编辑字段显示编辑态，同意时一并提交；隐藏字段不显示。
  - 轨迹中“修改了 金额、备注”，展开可看修改前后（有权限时）。

**7d 附件**
- Core：
  - `Files/IApprovalFileStore.cs`：`SaveAsync(Stream, ct) → storageKey`、`OpenReadAsync(storageKey)`、`DeleteAsync(storageKey)`。
  - `LocalApprovalFileStore`：配置 `RootPath`，按 `yyyy/MM/` 加 GUID 存储。
  - 实体 `ApprovalFile`（D17，含 StorageKey）。
  - `ApprovalFileService`：
    - `UploadAsync(userId, fileName, contentType, Stream)`：校验大小、扩展名，计算 SHA-256，文件名清洗。
    - `GetForDownloadAsync(fileId, viewer)`：按 D18 判断权限。
    - `BindToInstance`、`BindToDraft`。
    - `CleanupAsync`（D19）。
  - `ApprovalOptions.Files`：MaxFileSizeMb（默认 20）、AllowedExtensions（默认 常见办公文档与图片：pdf、doc、docx、xls、xlsx、ppt、pptx、txt、csv、png、jpg、jpeg、gif、zip）、MaxFilesPerField（默认 10）。
- `FormFieldType.File`，属性 `maxFiles`、`accept`（扩展名子集）。
- `FormDataValidator` 校验文件项结构；`ApprovalFormService` 提交时校验：文件存在、上传者为当前用户、未绑定到其他实例（重新提交、节点编辑时允许已属于本实例的文件）。提交后绑定。
- 接口：
  - `POST files`（multipart，`RequestSizeLimit` 按配置）→ `{id,name,size,contentType}`。
  - `GET files/{id}`（下载，D18 响应头）。
- 后台：`ApprovalTimeoutScanner` 每轮顺带清理过期未绑定文件，或新增 `ApprovalFileCleanupService`，二选一，执行时决定，倾向前者以减少后台服务数量。
- 前端：
  - `FormFieldInput` 支持 File（`el-upload`，自定义上传，带请求头）。
  - `FormValueText` 显示文件列表（下载链接走带鉴权的 fetch + blob，因为下载需要请求头）。
- 示例：`expense.json` 增加“发票附件”字段；`appsettings.json` 增加 `Approval:Files:RootPath`（默认 `App_Data/approval-files`，加入 .gitignore）。

### 3.5 管理功能（第 8 项）

**Core**
- `ApprovalEngine.Admin.cs`（partial）：
  - `TerminateAsync(instanceId, operatorId, reason)`（D21，必须填原因）。
  - `ReassignAsync(taskId, operatorId, toUserId, reason)`、`ReassignUserAsync(fromUserId, toUserId, operatorId, reason)`（D22；返回改派数量；跳过已结束实例；目标是发起人且不允许自审时跳过该条并在结果中说明）。
- `IApprovalBusinessHandler.OnTerminatedAsync` 默认实现调用 `OnRejectedAsync`。
- `ApprovalAction` 增加 `Terminate`、`Reassign`、`FormEdit`。
- `Engine/ApprovalStatisticsService.cs`：`GetStatisticsAsync(StatisticsQuery(from, to, definitionKey?))` → 汇总 DTO（D24）。
- 通知：终止触发 Completed（结果“已终止”）；改派触发 Transferred（接收人为新处理人），模板变量“操作人”为管理员。

**AspNetCore** `Endpoints/AdminEndpoints.cs`（全部 `EnsureApprovalAdminAsync`）：

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `admin/instances` | 分页搜索全部实例（状态、流程、发起人、关键字、时间） |
| POST | `admin/instances/{id}/terminate` | `{ reason }` |
| POST | `admin/tasks/{id}/reassign` | `{ toUserId, reason }` |
| POST | `admin/users/{userId}/reassign` | `{ toUserId, reason }` → `{ count, skipped }` |
| GET | `admin/statistics?from&to&definitionKey` | 统计 |

**前端**
- 新页面 `views/MonitorView.vue`（路由 `monitor`，菜单“流程监控”，仅管理员）：
  - 页签“实例”：筛选栏 + 分页表格 + 行操作（查看 / 终止）。
  - 页签“统计”：卡片（总数、通过率、平均耗时、超时数）+ 各流程表格 + 节点耗时表格 + 待办积压前 10。
  - 页签“离职交接”：选择原处理人 / 新处理人 → 预览待办数 → 确认。
- 审批详情：管理员看到“终止流程”按钮，以及每个待办任务旁的“改派”。
- `labels.ts` 增加新动作文字；`taskStatus` 不变。

## 4. 实施清单

### 阶段 0：准备
- [x] 确认是否提交上次未提交的修复（建议 `fix: discard partial data changes when actions fail`）；确认沿用“每阶段本地提交、不推送”。
- [x] 基线：`dotnet test -c Release`（96 + 14）、`cd web; pnpm typecheck; pnpm test`（32）。

### 阶段 1：数据权限（第 1 项）
- [x] `ApprovalCarbonCopy` 实体、表、索引；引擎抄送写入；测试：抄送用户 / 角色记录、退回后再次抄送记录新 Round。
- [x] `IApprovalAccessPolicy` + 默认实现 + `AddAccessPolicy<T>()`；`GetInstanceForViewerAsync`、`GetByBusinessForViewerAsync`。
- [x] `FlowDefinition.ToPublicView()`；定义接口改为仅管理员；`instances/{id}/definition`。
- [x] 示例证书申请详情接口改用带权限的查询。
- [x] 前端详情改用公开定义；非管理员隐藏流程定义菜单与设计器入口；无权提示。
- [x] 测试（Core）：发起人、用户处理人、角色候选人、实际处理人（转交后原处理人仍可看）、加签人、抄送用户 / 角色、无关用户、自定义策略覆盖。
- [x] 测试（AspNetCore）：无关用户 404、管理员 200、按业务查询被过滤、非管理员读定义 403、公开视图不含动作参数。
- [x] 验证：全部测试、`e2e-v3.ps1`、`e2e-forms.ps1`（脚本中用 alice 查看他人实例的地方需确认仍有权限）。

### 阶段 2：分页与筛选（第 2 项）
- [x] `Paging.cs`；查询服务改造与新增 `GetCarbonCopiedAsync`、`SearchInstancesAsync`、`GetTodoCountAsync`；索引。
- [x] 接口改为分页参数与 `PagedResult`；`cc`、`todo/count`；outbox 分页。
- [x] 前端 `usePagedList`、表格分页与筛选、“抄送我的”页签、待办角标；`MessageBell` 不变。
- [x] 更新 e2e 脚本中对列表返回结构的使用（`(Api GET '/approval/todo' …).items`）。
- [x] 测试：分页边界（page 越界、pageSize 上限）、关键字、状态筛选、角色待办计数、抄送我的（角色抄送）。

### 阶段 3：管理功能（第 8 项）
- [x] `ApprovalAction` 新值、`OnTerminatedAsync` 默认实现、`ApprovalEngine.Admin.cs`（终止、改派、批量改派）。
- [x] 统计服务；`AdminEndpoints`；前端 `MonitorView`、详情页管理员操作。
- [x] 示例：证书业务终止后申请单关闭、证书解锁（通过默认 `OnTerminatedAsync → OnRejectedAsync` 验证）。
- [x] 测试：终止（并行进行中、加签进行中、已结束实例报错）、改派（角色任务改派、改派后原处理人不能处理、时限不重置）、批量改派（跳过发起人自审的情况）、统计数字（构造固定时钟数据）、非管理员 403。

### 阶段 4：组织架构审批人（第 6 项）
- [x] `IApprovalOrganizationProvider`、3 个解析器、`AddOrganizationProvider<T>()`。
- [x] `ApproverTypeDescriptor`、`IApproverResolver.Descriptor` 默认实现、`meta` 返回描述（**前端类型同步修改**）。
- [x] 有序解析器与依次审批校验；Level 值校验；`@field` 基准。
- [x] 示例组织数据、`DemoIdentityProvider` 实现、`leave.json` 改造、新增 `purchase.json`。
- [x] 前端 `ApproverRulesEditor` 按 ValueKind 渲染、摘要文案。
- [x] 测试：各级部门负责人、无上级部门时为空（Error / AutoApprove 两种策略）、第 N 级上级、逐级审批顺序与去重、`@field` 基准、Level 值非法时发布失败、ManagerChain 在或签节点发布失败。

### 阶段 5：表单增强（第 7 项）
- [ ] 7a 草稿：实体、服务、接口、前端（保存、自动保存、草稿列表、提交删除草稿）；测试：保存不校验必填、每人上限、提交后删除、他人草稿 404、流程更新后打开。
- [ ] 7b 计算字段：C# / TS 公式引擎 + 共享用例；校验（语法、引用、循环）；规范化计算（含明细列公式与合计）；前端实时计算与属性面板；测试覆盖共享用例全部通过。
- [ ] 7c 节点字段权限：定义、发布校验、带表单的同意、`Details` 列、隐藏字段过滤、前端设计器与详情页；测试：只能改可编辑字段、改后条件分支按新值、非表单业务拒绝、隐藏字段对仅参与该节点的用户不可见而发起人可见、日志 Details 同样过滤。
- [ ] 7d 附件：存储接口与本地实现、文件服务、上传下载接口、字段类型、提交绑定、清理、前端上传与下载；测试：大小与扩展名限制、他人未绑定文件不可用、绑定后按实例权限下载、无关用户 404、清理只删过期未绑定文件、响应头。
- [ ] 示例流程更新（报销加发票附件与计算字段，如“报销金额 = 合计 - 预支”；请假“天数”可由“起止日期”计算——日期差需要函数 `days(period)`，**是否加入在执行时评估**，默认不做，以保持公式语法简单）。

### 阶段 6：联调、文档
- [ ] 浏览器端到端：
  - alice 保存草稿 → 继续编辑 → 带附件提交报销（计算字段与合计）。
  - 部门负责人（组织架构）审批并修改可编辑字段。
  - 隐藏字段对财务不可见。
  - bob 打开 alice 的详情显示“无权查看”。
  - admin 在监控页终止一条、改派一条、离职交接。
  - 统计页数字合理；工作台分页与“抄送我的”。
  - 证书业务回归；控制台 0 错误。
- [ ] 更新 e2e 脚本（分页结构、组织架构流程），全部跑通。
- [ ] README：
  - 数据权限（默认规则、自定义策略、404 约定、定义仅管理员）。
  - 分页参数、组织架构接入（`IApprovalOrganizationProvider`、三种审批人与 `@field`）。
  - 表单新能力（草稿、公式语法、字段权限、附件与安全配置）。
  - 管理接口、数据库变更与迁移说明、已知限制更新。
- [ ] `dotnet pack` 两个包、`pnpm build:lib`；乱码扫描、`git diff --check`、全部测试。
- [ ] 向用户报告（中文，按 改了什么 / 如何验证 / 注意事项 / 下一步）。

## 5. 验证命令

```powershell
cd D:\Study\CSharp\Workflow
dotnet test -c Release
cd web; pnpm typecheck; pnpm test; pnpm build; pnpm build:lib; cd ..
Remove-Item output/e2e/v5.db* -ErrorAction SilentlyContinue
$env:ConnectionStrings__Default='Data Source=D:\Study\CSharp\Workflow\output\e2e\v5.db'
dotnet build samples/Certificate.Api -c Release
dotnet run --project samples/Certificate.Api -c Release --no-launch-profile --no-build --urls http://localhost:5199
# 另一个终端：
./samples/Certificate.Api/smoke-test.ps1; ./output/e2e/e2e-v3.ps1; ./output/e2e/e2e-forms.ps1; ./output/e2e/e2e-outbox-retry.ps1
```

## 6. 执行环境注意事项（踩过的坑）

**编辑与编码**
- 生成中文时偶发 U+FFFD 乱码：每次写文件后用 Grep 搜 `\x{FFFD}` 并立即修正。
- 补丁优先用 Edit 工具或 `output/*.py` 脚本文件。
- Bash heredoc 中出现单引号会被截断：长 Python 脚本先 Write 成文件再运行。
- Git Bash 的 `/tmp` 与 Windows Python 路径不同，临时文件放 `output/`。
- 用 python `str.replace` 批量替换时注意**同一字符串多处出现**会被全部替换，后续再次匹配会失败。

**构建与运行**
- 一律 `-c Release`（Visual Studio 可能占用 Debug 输出）。
- 示例：5199 端口、`--no-launch-profile --no-build`、`output/e2e/*.db` 独立库；表结构变化后删库重建。
- 停服务用 `Get-NetTCPConnection -LocalPort 5199` 找到进程后 `Stop-Process`。

**PowerShell**
- 访问 localhost 要加 `-NoProxy`（本机有系统代理）。
- 中文请求体用 `[Text.Encoding]::UTF8.GetBytes(...)`。
- 命令里不要同时出现 `Remove-Item` 与含 `/xxx"` 的 URL 字符串（工具的安全检查会误判，拆成两条命令）。

**浏览器（playwright-cli）**
- `snapshot` 输出内联 YAML，用 `pw.ps1` 的 `snap` / `ref` / `find`，不要按修改时间取旧快照文件。
- Element Plus 下拉框用 `pick '标签' '选项'`（DOM 点击）；远程搜索的选择器需要先给输入框赋值并派发 input 事件。
- 截图只能保存到浏览器打开时所在目录（在 `output/e2e` 下打开）。
- 设计器有未保存修改时 `goto` 会被离开确认拦截，需 `playwright-cli close` 后重新 `open`。

**前端**
- 被子组件就地修改的表单数据要用 `ref`（深层响应），不要用 `shallowRef`。
- 库内部只用相对路径导入（宿主的 `@` 别名指向宿主自己的 src）。
- 新增的组件 / 函数如需对外使用，记得在 `index.ts` 导出。
- `useAsyncData` 等导出函数要写明确的返回类型（库的类型声明生成要求）。

## 7. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 权限收紧导致现有页面 / 脚本拿不到数据（如证书申请详情、e2e 中用 alice 查看） | 阶段 1 先补全测试覆盖各类参与者；运行全部 e2e；README 说明新规则与自定义策略 |
| 分页改变返回结构，宿主已有调用方受影响 | 包未发布；README 写明变更；前端与脚本同步修改 |
| 角色候选人判断需要逐个调用 `IsInRoleAsync` | 列表接口本身按用户过滤，只有详情判断单条；`GetRolesAsync` 一次取全部角色再在内存判断 |
| 公式两套实现结果不一致 | 共享用例文件，两边测试都必须全部通过；服务端结果为准 |
| 隐藏字段通过其他途径泄露（轨迹、通知模板、动作参数、标题模板） | 轨迹文本只写字段名，值放 Details 并过滤；文档提示“通知模板与标题不要引用隐藏字段”；发布校验对标题模板引用隐藏字段给出**警告**（不阻断） |
| 附件安全（越权、恶意文件、磁盘占满） | D18 / D19；默认扩展名白名单；文件不在 wwwroot；下载强制附件方式；大小与数量上限 |
| 终止触发业务回调失败导致无法终止 | 回调异常照常回滚并返回错误信息，管理员处理业务问题后重试；文档说明 |
| 统计在大数据量下慢 | 时间范围与条数上限；文档说明可替换为宿主报表 |
| 数据库新增表 / 列 | 全部为新增；示例检查并提示删库重建；README 给迁移说明 |

## 8. 回滚

每阶段一个本地提交，可单独 `git revert`。数据库变更全部为新增表 / 列 / 索引，回滚代码后旧代码不读取这些表，可以忽略或删除。草稿与附件文件位于独立目录，回滚后可整体删除。
