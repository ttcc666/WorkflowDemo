# Workflow Action Engine

在流程设计器中将动作节点拖到连线上，然后按执行顺序添加步骤。动作节点只有一条后继连线。流程协议沿用 `schemaVersion: 1`，每个步骤的 `configVersion` 绑定 Handler 配置版本。

| 类型 | 执行模式 | 用途 |
| --- | --- | --- |
| JavaScript | 同步 | 受限计算，输出 JSON 对象 |
| Mapping | 同步 | 按声明式字段规则转换 JSON |
| Sql | 同步 | 通过宿主注册的只读查询读取业务数据 |
| Http | 同步 GET 或异步 | 调用宿主批准的 HTTPS 目标 |
| Notification | 同步记录，外部渠道异步投递 | 复用审批站内信和宿主通知渠道 |
| 自定义 C# | 由 Handler 声明 | 宿主业务逻辑和扩展 |

`outputKey` 在整份定义中唯一。动作配置和模板用 `form.amount` 访问表单，用 `outputs.risk.level` 访问当前步骤可见的同步输出，还可用 `instance`、`business`、`initiator`、`node` 和 `operator`。路径支持点分属性和零基数组索引。`{{path}}` 单独占据 JSON 值时保留数字、布尔、对象和数组类型。嵌在文本中时渲染为文本。

条件表达式始终可直接访问表单字段，如 `amount > 5000`。含动作的流程可用 `workflow.form.amount` 和 `workflow.outputs.risk.level`，并保留没有同名字段时的 `form.*` 和 `outputs.*` 别名。存在同名字段时，裸字段值继续有效。旧的限定路径若有歧义，需用 `workflow.form` 或 `workflow.outputs` 明确来源。`workflow` 为含动作流程保留，表单字段冲突按不区分大小写检查。

单点 Action 预演从开始节点沿自动路径运行，累积可见的上游预演输出，到达目标后停止。人工审批、未选中的分支、上游失败和缺少预演结果会在路径上说明。异步动作可展示预演计划。Notification 的 `FormField` 接收人必须引用 `User` 或 `Users` 字段。Forms 入口会验证并规范化人员输入，直接调用 Core 的可信宿主负责提供经校验的表单快照。

Mapping 的每条规则包含 `source`、`target` 和可选 `transform`。可用转换包括 Copy、String、Number、Boolean、Date、Array 和 Object。`default` 处理缺失和空值，`omitNull` 省略空值，`precision` 指定小数位。Array 可以用 `filterPath` 和 `filterEquals` 过滤，Array 和 Object 可以通过 `items` 添加相对路径规则。

## 演示采购审批

示例定义在 `samples/Certificate.Api/ActionExamples/purchase-action.json`。开发环境中的 Certificate 示例宿主注册演示库存查询。ERP 目标由宿主指定，指向你控制的测试 HTTPS 地址。

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Certificate__DemoMode = 'true'
$env:Approval__Actions__ErpEndpoint = 'https://your-test-erp.example/orders'
dotnet run --project 'samples/Certificate.Api' -- --urls 'http://localhost:5199'
```

在第二个终端从仓库根目录发布示例并发起。

```powershell
$base = 'http://localhost:5199/api/approval'
$definition = Get-Content -LiteralPath 'samples/Certificate.Api/ActionExamples/purchase-action.json' -Raw
Invoke-RestMethod -Method Post -Uri "$base/definitions/purchase-action?name=Purchase" -ContentType 'application/json' `
  -Headers @{ 'X-User-Id' = 'admin'; 'Idempotency-Key' = [guid]::NewGuid().ToString('N') } -Body $definition
$started = Invoke-RestMethod -Method Post -Uri "$base/forms/purchase-action/start" -ContentType 'application/json' `
  -Headers @{ 'X-User-Id' = 'alice'; 'Idempotency-Key' = [guid]::NewGuid().ToString('N') } `
  -Body '{"formData":{"productCode":"P001","amount":58000}}'
$todo = Invoke-RestMethod -Uri "$base/instances/$($started.instanceId)/my-task" -Headers @{ 'X-User-Id' = 'dave' }
Invoke-RestMethod -Method Post -Uri "$base/tasks/$($todo.task.id)/approve" -ContentType 'application/json' `
  -Headers @{ 'X-User-Id' = 'dave'; 'Idempotency-Key' = [guid]::NewGuid().ToString('N') } -Body '{}'
```

管理员可在实例详情查看步骤记录，从同一页进入 Outbox。示例按金额计算风险，查询 P001 的库存，把数据映射到 ERP JSON，审批通过后安排 ERP 同步并写站内消息。自定义 C# 的可运行归档示例位于 `eng/consumers/EmbeddedHost`，由包验证脚本执行。
