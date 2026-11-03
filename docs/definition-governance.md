# 流程设计、发布与启停

设计草稿、不可变发布版本和允许新发起的状态分别保存。业务表单草稿仍使用原 `ApprovalDraft`，与这里的流程设计草稿无关。

```mermaid
flowchart LR
    UI["DesignerView.vue"] --> D["SaveDesignDraftAsync<br/>草稿 + 编辑令牌"]
    D --> P["PublishDesignDraftAsync<br/>校验 + 发布计数 + 审计"]
    P --> V["ApprovalDefinition<br/>不可变发布版本"]
    P --> M["ApprovalDefinitionManagement<br/>选用版本 / 启停状态"]
    M --> S["ApprovalEngine / ApprovalFormService<br/>新发起使用选用版本"]
    V --> I["已有 Instance.DefinitionId<br/>继续读取原流程与 Schema"]
```

## HTTP 与 Core 接入

下面的路径均相对于 `MapApprovalEndpoints` 的路由前缀。写操作使用 `Idempotency-Key`；需要严格拒绝缺键时开启 `RequireOperationId`。Core 复合调用通过 `IApprovalCommandExecutor` 包裹完整委托。

| 接口 | 操作 / 权限 |
|---|---|
| GET `/definitions/governance` | 包含仅有草稿的流程清单及逐资源 capabilities；`definition.read-sensitive` |
| GET `/definitions/{key}/governance` | 设计草稿、状态、编辑令牌；`definition.read-sensitive` |
| PUT `/definitions/{key}/draft` | `{ name, definitionJson, expectedRevision, sourceDefinitionId }`；`definition.design` |
| POST `/definitions/{key}/publish-draft` | `{ expectedRevision, changeNote }`；`definition.publish` |
| POST `/definitions/{key}/enabled` | `{ enabled, expectedRevision, changeNote }`；`definition.disable` |
| POST `/definitions/{key}/select-version` | `{ definitionId, expectedRevision, changeNote }`；`definition.publish` |
| GET `/definitions/{key}/audit?page=1&pageSize=20` | 同事务治理审计；`definition.read-sensitive` |
| GET `/definitions/synchronous-actions?page=1&pageSize=20` | 旧动作只读扫描；同时要求 `diagnostics.read` 与 `definition.read-sensitive` |

表中的权限省略共同前缀 `approval.`。启停和选用版本的写响应不返回保存的草稿 JSON；拥有操作权限不自动获得敏感定义读取权限。

新流程首次保存使用 `expectedRevision=0`。从读取结果获取后续令牌，每次治理写入递增令牌；其他人已保存、发布或启停时返回 409，重新读取后检查差异再提交。草稿只解析 JSON，允许保存尚未通过结构校验的设计；发布始终执行完整校验。

发布号由独立管理记录中的持久计数及并发令牌控制，唯一约束提供最终防线。并发初始化或发布的失败方整体回滚并返回冲突；不会留下失败审计、命令结果或覆盖获胜版本。首次治理写入旧流程时从现有最大版本初始化计数，不虚构旧发布人或旧审计。

原 `PublishAsync` 和 POST `/definitions/{key}` 继续可用，并接入相同计数和审计。旧宿主先校验后显式传递 `StartApprovalRequest.DefinitionId` 的绑定语义保留；未显式绑定的新发起使用当前选用版本。两条路径都受停用状态限制。

## 草稿、历史与恢复

保存草稿不会改变新申请使用的发布版本。发布会生成新版本并选用它，保留当前启停状态；发布停用流程不会自动启用。选用历史版本和重新启用均为显式权限操作并留下审计。历史 JSON、在途实例的 DefinitionId 和原表单 Schema 不变。

`Revision` 是所有治理操作的并发令牌，`DraftRevision` 与 `PublishedDraftRevision` 用于判断草稿是否有未发布内容；启停操作本身不会制造草稿修改。发布版本禁止通过 SaveChanges 或 EF 批量更新改写，治理审计禁止改写或删除。

审计保存可信操作人、UTC 时间、动作、版本/来源 ID、变更说明及定义摘要，不保存动作参数或凭据。读取完整草稿及版本仍要求敏感定义权限。外部日志或通知由宿主登记 `AfterCommit` 或使用 Outbox；外部调用不属于本地审计事务。

设计器支持保存后刷新恢复草稿、发布变更说明，以及在“版本与治理”中启停、选择历史版本和查看审计。按钮采用资源 capabilities，实际提交仍由 Core 复核。网络错误或待确认结果保留原键与原内容；同页重试不重复保存或递增发布版本。刷新后若当前令牌已经变化，先核对审计/版本中的原结果，再显式开始新操作，不能盲目生成新键重做。

## 流程分类

`settings.category` 在保存草稿、发布和选择版本时同步到数据库查询投影。列表有草稿时显示草稿分类，否则显示选用发布版本分类；空白归为未分类，trim 后最多100字符且拒绝控制字符。

`GET /definitions/governance?category=财务` 与 `?uncategorized=true` 在 SQL 筛选，两者不能同时指定。`GET /definitions/categories` 只返回当前用户有权读取流程的分类。发布不覆盖现有草稿；复制仅创建新标识的草稿。

## 当前初始化与协议

项目尚未正式使用。九套上下文各只有一条 `InitialApproval`，直接创建当前模型（包括分类、治理、抄送阅读和事件表），没有历史回填或升级前置检查。发布和保存草稿通过服务创建治理记录；缺少治理记录的定义不会自动被视为已启用。

通用动作类型、DTO、扫描 API 和 UI 已删除。JSON 中未知节点类型、数字枚举或未定义节点属性会被拒绝；没有只读旧 Action 模式。宿主本地准备使用 `IApprovalLifecycleHandler`，结束业务用 `IApprovalBusinessHandler`，远程副作用用 `IApprovalEventHandler`。

业务中的不可变历史版本、轨迹和审计仍保留，这是正常审批功能，与旧环境兼容无关。
