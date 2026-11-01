# 注册证三级审核本地试点

本试点采用 SQLite 和 Development 演示身份。业务路径为 **经办人 → 部门负责人 → 质量/法规 → 负责人终审 → 正式台账**。正式证书仅在终审通过后写入，驳回后可复制资料重新申请；新申请重新走完整审核链。

## 启动与验收

仓库通过 global.json 固定 .NET 10.0.401，通过 packageManager 固定 pnpm 10.20.0；前端使用 Node 24。以下命令从仓库根目录执行：

```powershell
dotnet tool restore
dotnet restore 'Workflow.slnx'
dotnet test 'Workflow.slnx' --verbosity 'minimal'
Push-Location 'web'
pnpm install --frozen-lockfile
pnpm typecheck
pnpm test
pnpm build
Pop-Location
dotnet run --project 'samples/Certificate.Api' --launch-profile 'http'
```

浏览器访问 http://localhost:5199。默认本地库为 `samples/Certificate.Api/App_Data/registration-pilot.db`，由 EF Migrations 创建，和旧的演示库分开。

另一个终端运行明确指定地址的验收：

```powershell
pwsh -File 'samples/Certificate.Api/smoke-test.ps1' -Base 'http://localhost:5199'
```

脚本使用唯一证号和提交标识，始终按本次 instanceId 获取待办；验证匿名/越权拒绝、重复提交、三审前后台账和历史快照，意外错误返回非零退出码。它会在明确指定的本机试点库中留下演练记录，不会假定空库或选择用户的第一条待办。

| 演示身份 | 作用 |
|---|---|
| alice | 登记人 |
| dave | alice 所属部门的负责人，初审 |
| quinn | QualityManager，复审 |
| leo / lily | Leader，终审任一人 |
| bob | 无关用户/并发场景 |
| admin | 运行诊断、受控改派和终止 |

演示模式同时要求 Development 和 `Certificate:DemoMode=true`，HTTP 访问限回环地址。非演示模式必须由宿主注册真实认证、身份提供者和组织架构；系统不会把 X-User-Id 当成生产身份。`AddCertificateApplication` 的 configureApproval 回调可用于接入组织架构等扩展。

## 提交协议与权限

- 新增、变更和注销必须携带 GUID 格式的 `Idempotency-Key`。相同申请人/操作/键和相同规范化内容返回同一 requestId；同键不同内容返回 409。网络失败后应使用原键和原内容重试。
- 数据库的 CertificateNumberReservations 主键保证同一规范化证号只被占用一次。终审后继续保留已登记归属；驳回、撤回、终止仅释放该申请尚未登记的占用。
- 证号键采用 Trim + ToUpperInvariant；展示证号保留首尾去空白后的原值。证号最多 100 字，名称/持有人最多 200 字，注销原因 1–1000 字。
- 服务端校验字段、类型和日期；正常登记不接受已过期证书，最终生效前再次校验。到期日包含当天，业务日期统一按 Asia/Shanghai 计算，时间戳保存 UTC。
- 申请人、实际审批参与者、当前候选人及管理员按既有审批规则查看单据。列表、历史、详情均在服务端过滤；无关用户查单条返回 404。登记人或管理员才可申请变更/注销。
- 试点关闭普通转交、加签和原地退回。管理员改派需要理由，目标必须是存在且具备当前节点资格的用户；角色/部门变化后，实际审批时也会重新校验资格。
- 同一人不能完成一个实例中的多个不同审核节点。发起前检查必要岗位及人员分配，运行期间发现失去资格的待办会显示在诊断中。
- 所有新提交的 BeforeSnapshot / Snapshot 固定保存。历史页的“提交前”不会随正式证书后续变化而改变；旧数据没有基线时明确显示“历史基线未记录”。

业务读取接口的列表现在返回 `{ items, total, page, pageSize }`：

| 接口 | 用途 |
|---|---|
| GET /api/certificates?page&pageSize&keyword | 可见台账；包含 effectiveStatus 和 canManage |
| GET /api/certificates/{id} | 可见的单条证书 |
| GET /api/certificates/{id}/requests?page&pageSize | 可见的申请历史 |
| GET /api/certificate-requests/{id} | 申请快照、固定差异、审批与 canReapply |
| POST /api/certificates | 新增登记；可带 sourceRequestId 关联自己的已结束申请 |
| PUT /api/certificates/{id} | 变更；证号不可更换 |
| POST /api/certificates/{id}/revoke | 注销；传 reason，可带 sourceRequestId |

## 流程发布与版本

启动只补齐缺失的流程，不覆盖管理员已经发布的定义。修改磁盘 Flows 文件之后，需要通过管理接口/设计器或显式发布命令生成新版本：

```powershell
dotnet run --project 'samples/Certificate.Api' --launch-profile 'http' -- '--Certificate:DatabaseOperation=publish-flows'
```

相同内容和名称不会重复发布。新申请绑定已经通过预检的 DefinitionId，避免预检与启动之间被另一次发布替换；运行中的实例继续沿用原定义。注册证登记入口要求完整的固定三审与岗位保护；旧库升级后如仍是旧版流程，应先显式发布新模板。

## 数据库备份和旧库升级

维护操作不会启动 HTTP 服务。目标必须是新的文件，程序拒绝覆盖现有目标或源库。命令输出目标路径与记录数量。

备份当前试点库（先完成一次构建，路径示例按当前仓库）：

```powershell
$pilotSource = 'D:\Study\CSharp\Workflow\samples\Certificate.Api\App_Data\registration-pilot.db'
$pilotBackup = 'D:\Study\CSharp\Workflow\output\registration-backup-' + [Guid]::NewGuid().ToString('N') + '.db'
dotnet 'samples/Certificate.Api/bin/Debug/net10.0/Certificate.Api.dll' '--Certificate:DatabaseOperation=backup' ('--ConnectionStrings:Default=Data Source={0}' -f $pilotSource) ('--Certificate:Destination={0}' -f $pilotBackup)
```

备份使用 SQLite Backup API，包含 WAL 中已提交的数据，并检查完整性。恢复时保留备份，把它复制/备份到另一新路径，再将连接字符串指向恢复副本；不要把运行中库的单个 .db 文件直接覆盖回来。

旧 EnsureCreated 库或带业务数据的旧迁移库必须先停止旧服务写入，再显式升级副本。以下从旧示例配置读取连接字符串；目标文件唯一，不覆盖任何已有库：

```powershell
$pilotUpgrade = 'D:\Study\CSharp\Workflow\output\registration-upgraded-' + [Guid]::NewGuid().ToString('N') + '.db'
Push-Location 'samples/Certificate.Api'
$legacyConnection = (Get-Content -Raw 'appsettings.json' | ConvertFrom-Json).ConnectionStrings.Default
dotnet 'bin/Debug/net10.0/Certificate.Api.dll' '--Certificate:DatabaseOperation=upgrade' '--Certificate:ConfirmOffline=true' ('--ConnectionStrings:Default={0}' -f $legacyConnection) ('--Certificate:Destination={0}' -f $pilotUpgrade)
Pop-Location
```

升级先检查已知模式、迁移历史、外键完整性、规范化证号冲突、申请实例关联和编辑锁。失败不会修改源库，也不会自动合并或删除记录。通过预检后复制源库，在副本接纳基线、执行迁移并回填占用与关联；无法还原的历史 BeforeSnapshot 保持未知。升级过程中失败的副本可保留用于排查，不应作为业务库启动。

切换前必须核对命令结果、台账数量、申请和待办，再启动指向新文件的本地服务：

```powershell
dotnet run --project 'samples/Certificate.Api' --launch-profile 'http' -- ('--ConnectionStrings:Default=Data Source={0}' -f $pilotUpgrade)
```

不要直接用 `dotnet ef database update` 跳过旧库预检。`CertificateBaseline` 记录历史模式，`CertificatePilotHardening` 增加本次约束；旧库的数据关联/占用回填由显式升级操作完成。

## 运行检查与故障处理

- `/health/live`：进程存活；`/health/ready`：数据库连接、迁移和关键关联就绪。健康响应不返回连接字符串。
- 管理员访问 `/api/approval/admin/certificates/diagnostics` 或注册证页面的“试点运行检查”：查看待审数量、失败投递、超时及失去资格的待办。岗位检查一次展示最早 100 条，超过时明确标记 truncated。
- CertificateMonitor 默认每 5 分钟检查一次，异常状态变化时记录结构化告警；可接入宿主日志/告警平台。`Certificate:MonitorInterval` 可调整。
- 请求响应包含 X-Trace-Id，日志带 TraceId/SubmissionId；提交日志记录 requestId 与 instanceId。Outbox 失败保留 outboxId、重试次数和错误。
- 所有后台工作受 `Approval:EnableBackgroundServices` 控制。多副本共享库时可限制后台实例数量；租约与并发令牌仍负责消息抢占。SQLite 是本地试点的单写者数据库，不把本轮结果推定为其他数据库的验证。
- 投递失败：核对接收方、错误及稳定幂等键，通过投递记录页重试；不要重新发起业务审批来补通知。
- 人员变化：先修正组织/岗位配置，再由管理员填写理由改派到合格人员；时限不会重置。
- 终审时资料已过期：业务写回会失败且原待办保持；驳回并重新申请，或由管理员终止，不直接改数据库状态。
- 数据库忙返回 503 / DatabaseBusy / Retry-After；客户端保留原提交标识后重试。409 表示明确的业务/并发冲突，两者不能混为一谈。
- 恢复后核对正式证书、申请、审批任务、证号占用和投递记录。新模式上线后不自动执行破坏性 Down；保留源库和备份，必要时回到兼容版本/另一恢复文件。

## 自动化证据

`tests/Certificate.Api.Tests` 通过真实服务注册和 HTTP 管线、SQLite、实际 CertificateApprovalHandler 验证业务。并发用同步屏障让两个独立 DbContext 到达保存边界；恢复测试重建宿主/服务提供者，租约恢复用持久化的已抢占状态验证边界；没有将这些用例描述为操作系统进程强杀或硬件故障测试。

`.github/workflows/ci.yml` 在 Windows 上执行全部后端测试、迁移漂移检查、前端测试/类型检查/构建及冒烟脚本语法检查。CI 文件的实际远端运行需在推送后由 GitHub 执行；本地验证结果见实施记录。
