# 事务、幂等与恢复

ApprovalUnitOfWork 使用宿主注册的同一个 DbContext。本地业务、审批实例/任务、追加审计、操作记录和 Outbox 统一提交。任一操作失败会回滚事务并使该 scope 作废；宿主必须释放 scope，不能继续保存残留实体图。

命令包含 Kind、Scope、OperationId 和 Payload。执行器按操作人/命令类型/范围/键生成 LookupHash，保存 RequestHash、版本和结果 JSON。唯一索引在执行宿主委托前预约键，防止并发重复请求都执行成功。相同键与不同内容返回 Conflict；相同内容恢复原结果及绑定版本，并重新检查当前权限。

数据库 COMMIT 之后发生异常时，用新上下文及独立超时核验记录。无法确认时返回 CommitOutcomeUnknown，客户端保留原键重试。不能把提交未知当成确定失败并换新键。完整委托由数据库执行策略重试，每次使用新 scope。

## 生产配置与 HTTP 调用

生产部署建议显式开启严格模式，避免双击、网络重试或未知提交结果被误认为新的业务操作：

```csharp
services.AddApprovalWorkflow<AppDbContext>(options =>
{
    options.RequireOperationId = true;
});
```

默认值仍为 false；无键调用不提供请求级 Replay 保障，业务唯一占用仍由数据库约束保护。ResultJson 与 LookupHash 分别用于提交恢复和避免数据库字符串排序规则破坏键身份。

调用方在一次业务意图创建时生成键，保存键与原请求，重试时发送同一个 HTTP `Idempotency-Key`，不能在每次发送时生成新键：

```http
POST /api/approval/tasks/00000000-0000-0000-0000-000000000001/approve
Authorization: Bearer <宿主签发的令牌>
Idempotency-Key: 1af811fb-5533-4a45-9547-29e3c398d972
Content-Type: application/json

{"comment":"同意"}
```

普通审批命令键是 1–200 个非控制字符，不能为空白；不要求 UUID，也不自动 trim 或折叠大小写。只能发送一个 Header 值。示例证书提交 API 另有非空 UUID 要求，以该宿主契约为准。

| 情况 | 实际行为 |
| --- | --- |
| 相同操作人、Kind、Scope、Key，且请求摘要相同 | 已提交记录 Replay，返回原状态码／结果（包括 204），不再次执行委托；重新检查当前读取／资源权限，权限撤销后不能读取原结果 |
| 同一键范围，请求摘要不同或记录版本不受支持 | `409 Conflict`，ProblemDetails 的 `code` 为 `Conflict`，不执行新请求 |
| 开启严格模式，受管理的写命令缺少 Key；或 Key 空白、过长、含控制字符／多个 Header 值 | `400`，`code=OperationIdRequired`；身份与授权失败可能先返回 401／403 |
| 提交核验不可用 | `503`，`code=CommitOutcomeUnknown`；保留原键和内容，再次查询／重试 |
| 原操作事务确定回滚，如字段校验失败 | 不留下已提交操作记录；修正请求后可使用原键提交 |

键范围包含操作人、命令类型和资源范围，不是整个系统的全局 Key。HTTP 摘要覆盖绑定参数、查询参数和定义发布原始 JSON；JSON 对象属性排序及可解析 decimal 数值会规范化，数组顺序仍有意义。重试应保留原请求内容、路由和查询，不依赖规范化来改变请求。

唯一索引在宿主委托执行前预约键；并发同内容提交只能产生一个已提交结果。事务回滚／数据库策略可能重试完整委托，委托仍须允许重复执行。外部副作用应写 Outbox 后由宿主幂等处理。

严格模式核验的是命令执行器已在事务中预约带键操作的来源，单纯 `IApprovalUnitOfWork.IsActive` 不足以替代 OperationId。直接应用服务调用同样受检查；宿主业务写入和多个引擎调用放在同一个带键 executor 委托中。系统操作仍只接受显式允许的能力。

批量离职改派新增保留原签名的 OperationId 重载，并支持 HTTP Idempotency-Key。执行器分别保存候选清单、每个实例的结果和最终汇总；每个实例仍独立提交，部分完成后用原键恢复，不重新查询已改派后的候选来返回空结果。actor/kind/scope/key 定义键范围，相同范围/键不同内容冲突；当前权限撤销后仍不能读取旧结果。

草稿、表单提交/编辑、阅读状态、人工 Outbox Retry 和定义治理使用命令规则。只读/定义校验不要求键。文件上传的 Stream/FileStore 仍是明确的非请求幂等例外，保留存储失败补偿，不宣称数据库事务能回滚外部存储或实现跨系统 exactly-once。

## 批量操作、SDK 与非 HTTP 宿主

离职交接 `POST /api/approval/admin/users/{userId}/reassign` 传同一个 `Idempotency-Key` 与 `{ "toUserId": "carol", "reason": "离职交接" }`。C# 使用带键重载：

```csharp
await engine.ReassignUserAsync("bob", "carol", "admin", "离职交接", operationId, ct);
```

一次批次只生成一个键，候选计划、逐实例结果和最终汇总用不同 Kind／Scope 保存该键。部分完成后继续用原键与内容；已提交项目 Replay，尚未完成项目继续执行。批量不是整批原子事务，不得放入外层 UnitOfWork 或携带未提交的跟踪修改。结果中的 Skipped 表示业务／权限／冲突原因，不表示没有发生其他实例的提交。

Vue SDK 的命令方法接受 operationId，例如：

```typescript
const operationId = crypto.randomUUID() // 在创建意图时保存，重试复用
await client.api.approve(taskId, '同意', undefined, operationId)
```

当前 `client.api.reassignUser` 没有显式键参数。严格模式的批量调用使用客户端已提供的 HTTP 入口：

```typescript
await client.http.post(
  '/api/approval/admin/users/bob/reassign',
  { toUserId: 'carol', reason: '离职交接' },
  { 'Idempotency-Key': operationId },
)
```

路径以宿主实际 endpoint 前缀为准。SDK 不替调用方持久保存跨页面／跨进程的意图键；网络失败后保留原键，确定发起新意图时才换键。

非 HTTP 宿主通过 IApprovalContextResolver 提供可信操作人，然后使用命令执行器：

```csharp
var instanceId = await commands.ExecuteAsync(
    new ApprovalCommand("invoice.start", "invoice/" + invoiceId, operationId,
        new { invoiceId, amount }, ApprovalPermissions.InstanceStart,
        new ApprovalResource { DefinitionKey = "invoice" }),
    async (sp, token) =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        // 在这里使用 db 保存本地业务变化，不捕获原 scope 的服务或实体。
        var engine = sp.GetRequiredService<ApprovalEngine>();
        var instance = await engine.StartAsync(new StartApprovalRequest(
            "invoice", "Invoice", invoiceId.ToString(), actorId), token);
        return instance.Id;
    }, ct);
```

Kind、Scope、Key 与 Payload 必须稳定；Payload 要包含所有影响业务决策的输入，执行器不会分析委托来补充摘要。结果使用可序列化 DTO。严格模式下未提供键、也未经过带键 executor 的普通写服务调用会抛出 `ApprovalException`（`OperationIdRequired`），裸 UnitOfWork 不能代替已预约的带键命令。StartApprovalRequest 的 OperationId 入口也由执行器协调；后台系统操作仅在显式能力集合内运行。

没有操作过期、自动结果擦除或后台保留调度。IsTombstone 仅兼容旧版本已擦除的结果，新引擎不产生标记；此类旧键仍拒绝重执行业务，返回旧结果不可恢复的明确错误。

Outbox 是至少一次。数据库事务保证本地一致性，外部邮件/消息/推送等副作用由宿主基于 Event.Id 去重；不承诺跨系统 exactly-once。
