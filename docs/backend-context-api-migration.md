# 审批上下文 API 迁移

2026-11-03 用户已明确授权直接迁移当前未发布版本的 C# 扩展接口。宿主需要重新编译；HTTP JSON、数据库结构、状态机、OperationId 和 Outbox 协议保持不变。

## 签名变化

| 入口 | 原输入 / 返回 | 当前输入 / 返回 |
| --- | --- | --- |
| `IApprovalBusinessHandler.OnApproved/Rejected/Withdrawn/TerminatedAsync` | `ApprovalInstance` | `ApprovalInstanceContext` |
| `ApproverResolveContext.Instance` | `ApprovalInstance` | `ApprovalInstanceContext` |
| `ApproverResolveContext.Node` | 可变 FlowNode | ApprovalNodeContext：Id / Name / Type / Mode |
| `IApprovalLifecycleHandler.OnStartingAsync` | Entity 输入，返回 Task | Context 输入，返回 `Task<ApprovalStartPreparation>` |
| `IApprovalAccessPolicy.GetAccessAsync` 及默认实现 override | `ApprovalInstance` | `ApprovalInstanceContext` |
| `IApprovalDataProjection.Project` | `ApprovalInstance` | `ApprovalDataProjectionContext`，包含实例与审计快照 |
| `ApprovalDataView.Logs` | ApprovalLog 列表 | ApprovalLogContext 列表 |
| `ApprovalEngine.ApproveWithDataAsync` 编辑委托 | Entity 实例 / 任务 | Instance / Task Context；流程仍是独立副本 |
| `ApprovalEngine.FindHandleableTaskAsync` | `ApprovalTask?` | `ApprovalTaskContext?` |
| `InstanceDetail.Tasks / Logs` | Entity 列表 | Task / Log Context 列表 |
| `IApprovalTaskDataAdapter.GetEditableFieldsAsync`、Forms 对应入口 | ApprovalTask | ApprovalTaskContext |
| `ApprovalTaskResponse.From`、`ApprovalLogResponse.From` | EF Entity | 对应 Context；输出 JSON 不变 |
| `FieldVisibility.FilterLogs` | Entity 审计列表 | 审计快照列表 |
| `ApprovalQueryService.GetInstanceAsync / GetByBusinessAsync` | EF Instance / List | InstanceDetail / List，敏感权限仍单独检查 |
| `ApprovalQueryService.GetPublicDefinitionAsync` | EF Definition | ApprovalDefinitionSnapshot，内容仍裁剪为公开流程视图 |

IApprovalResourceResolver 已使用 ApprovalResource，保持接口。IApprovalQueryScope 的 IQueryable 用于数据库范围组合，保留该能力。审批模型实体仍用于共享 DbContext；扩展回调不会得到跟踪图、StateJson、ConcurrencyStamp 或 LogSequence。

实例快照提供实例 / 定义标识、业务类型和键、标题、发起人、状态、节点、Round、业务 JSON 和时间。需要更多业务数据时通过宿主业务 DbContext 读取；resolver 规则值仍由 ResolveAsync 参数提供。

`ApproverResolveContext.FormData` 由引擎在每次 Resolver 调用前创建独立深拷贝，顶层 `ReadOnlyDictionary` 即使转为 `IDictionary` 也不能增删改。嵌套对象保持 `ExpandoObject` 动态读取，数组保持 `object?[]`，数值保持 decimal / double；嵌套值允许在本次快照内修改，但不会影响其他 Resolver、条件表达式使用的数据或实例保存的 `FormDataJson`。此契约也适用于同一 Resolver 的重复调用和通知收件人解析；`FormData.Parse` 的既有解析行为不变。

业务、准备、权限和规则回调仍运行在原事务和安全边界内。宿主可修改自己的业务实体，不能修改 Core 审批实体、提前保存或自行提交事务。快照保持调用时的数据，不随引擎继续推进而改变。

## 开始准备

```csharp
public Task<ApprovalStartPreparation> OnStartingAsync(
    ApprovalInstanceContext instance, CancellationToken ct)
{
    // 校验或修改宿主业务实体，不调用 SaveChanges。
    return Task.FromResult(new ApprovalStartPreparation(instance.Title, instance.FormDataJson));
}
```

返回完整标题与数据。保持原值表示不修改；Title=null 表示清空。Core 在回调结束后应用这两个字段，再执行条件判断和任务创建，由原工作单元提交。

## 审批中编辑

```csharp
return new ApprovalDataEdit(normalizedJson, summary, changesJson)
{
    ReplaceTitle = true,
    Title = renderedTitle
};
```

直接给 instance.Title 赋值改为返回标题。ReplaceTitle=false 保持原标题，true 可设置新值或清空；返回 null 仍表示无编辑。Forms 字段 / 数据 / 附件校验保留，标题由 Engine 应用。

## 投影与审计

读取 context.Instance.FormDataJson 和 context.Logs，返回 ApprovalDataView。ApprovalLogContext 是 record，用 `log with { Comment = ..., Details = ... }` 生成过滤副本，可保留 TaskId、Sequence、时间而不重复复制其他字段。Query 提供只读任务 / 日志集合和独立流程副本。

只读接口不等于任意反射代码无法改 DTO；保护来自值复制及 Core 写入所有权，DTO 不代替 WriteGuard。

已迁移 Core、Forms、HTTP mapping、CertificateApprovalHandler、默认访问策略派生实现及所有受影响测试 fixtures。未保留并行旧接口或无用途的内部包装。历史审计描述原 HEAD；实际结果见逐轮报告。
