# 异步校验、默认严格幂等与统计资源边界修复计划

状态：全部完成。统一严格幂等、契约、文档和完整 verify 通过；MySQL / SQL Server / PostgreSQL 真实数据库各 56 项测试通过，均无失败或跳过。续作证据见 `docs/async-validation-idempotency-statistics.md`。

## 继续实施的确认范围（2026-11-04）

用户在「分析当前项目」明确说明项目处于开发阶段、不需要旧版本兼容，并选择“删除开关，统一严格幂等”。本轮继续完成该已授权变更；以下决定覆盖原方案中关于显式 false 和保留旧构造器的条款。

- 删除 `ApprovalOptions.RequireOperationId`、事务状态中的同名字段及 `ApprovalCommand.AllowUnkeyed`，命令执行器只处理合法稳定键。
- 普通用户写入必须处于已预约键的命令事务；裸 UnitOfWork 不能代替。显式系统能力、只读、定义校验及文件上传保留原边界。
- 删除批量改派的无键重载和全部无键分支，保留原键恢复候选清单、逐实例提交、最终汇总和当前权限检查。
- 删除统计服务自行创建 ApprovalOptions 的旧构造器，统一使用宿主预算。
- 真实用户测试迁移到带键调用；算法夹具使用明确系统作用域；缺键与非法键回归仍走生产门禁并验证无副作用。
- 同步 API/OpenAPI/TypeScript 契约和文档；不新增 Migration、依赖、框架升级，不提交、推送或发布。
- 验证：针对性权限/治理/严格边界测试 → 完整 verify → 三种真实数据库。失败只修复本轮调用迁移，不放宽规则。
- 回滚：本轮无数据库结构变化，按独立 diff 审查；不自动 reset/clean/revert 既有工作区。

## 目标与范围

用户要求修复三项：同步 Validate 阻塞异步授权；RequireOperationId 默认关闭；统计任务无界加载与积压全量枚举。

本任务会改变公开 C# API、默认写入行为和统计超限行为。用户后续明确授权“按计划实施”，本会话已执行；下方保留原检查清单，具体完成证据与未执行项见文末。

### 已确认的代码依据

- `src/Approval.Core/Engine/ApprovalDefinitionService.cs:72`：Validate 使用 RequireAsync(...).GetAwaiter().GetResult()；现有应用调用已经使用 ValidateAsync，静态 FlowDefinitionValidator.Validate 是另一个不需要修改的纯校验入口。
- `src/Approval.Core/ApprovalOptions.cs:8`：RequireOperationId 没有初始化值，默认 false。
- `samples/Certificate.Api/CertificateApplication.cs:73`：读取配置时也显式回退到 false，仅修改 Core 不足以改变示例宿主默认值。
- `src/Approval.Core/Engine/ApprovalStatisticsService.cs:76`：最多选择 100,000 个实例；任务查询只按最后实例的 CreatedAt 截断，再加载所有匹配任务，在内存过滤实际实例 ID。同一时间戳下可能读入大量未选实例的任务。
- 同一文件 BacklogAsync：逐行枚举所有匹配待办，在 C# 按处理人分组后取前 10；AsAsyncEnumerable 避免保留原始行，但不限制总读取行数或处理量。
- `tests/Approval.Core.Tests/RuntimeQueryOptimizationTests.cs` 和 `tests/Approval.Database.Tests/QueryOptimizationDatabaseTests.cs` 已要求 bob 与 BOB 分组独立，保留 nullable 时限、最早时间及结束实例/取消任务排除规则。

## 设计决定与不变量

1. **只保留异步的服务级授权校验入口。** 删除 ApprovalDefinitionService.Validate(FlowDefinition)，保留 ValidateAsync 及其权限、取消、扩展隔离。项目尚未发布，此处接受明确的源码兼容变化，并同步公开 API 基线和迁移说明。不通过 Task.Run、线程池包装或省略权限来保留同步表象。
2. **统一严格幂等。** 删除 RequireOperationId 开关和无键兼容分支。HTTP、SDK、直接引擎调用均保持原键格式、作用域、摘要、Replay 与未知提交恢复协议。只读、定义校验、显式系统能力及既有文件上传例外不扩大、不缩小。
3. **统计采用有界精确计算，超限明确失败。** 保留现有响应 DTO 和实例截断标志的含义；任务/积压达到资源预算后返回明确错误，不把部分计数伪装成全量结果。
4. 保留权限和 QueryScope、流程筛选、统计区间 [From, To)、最多 366 天、现有状态分布/耗时/P90/超时定义。Backlog 仍表示当前积压，不受发起时间区间影响。
5. 保留 Ordinal 处理人分组，包括大小写、重音、尾随空格等区别。不直接用宿主默认 collation 下的 SQL GROUP BY 替换当前语义。
6. 不新增表、迁移、Repository、预聚合后台任务或外部依赖，不修改审批运行、日志并发和 Outbox 规则。

### 统计方案与取舍

准确统计所有待办通常仍要求数据库读取所有匹配记录，或维护预聚合。本轮解决的是无界应用读取、内存和处理量，不承诺消除数据库执行计划中的物理扫描。

采用最小的独立统计配置 `ApprovalStatisticsOptions`，挂在 `ApprovalOptions.Statistics`：

- `MaxTaskRows` 默认 250,000：所选实例关联任务的最大读取预算。
- `MaxBacklogRows` 默认 100,000：当前匹配待办的最大读取预算。
- 两项必须大于零且小于 int.MaxValue，保证 limit + 1 安全；加入与现有配置一致的启动校验。它们是保护阈值，不是经过压测得出的容量承诺；测试可配置小阈值覆盖边界。

实例按 CreatedAt、Id 稳定排序，保持“最早 100,000 个实例”的规则。任务查询在数据库中 join 同样排序/Take 的实例子查询，确保不会因 CreatedAt 相同而加载未选实例的任务，不生成巨大的内存 ID IN 列表。

任务只投影必要列，数据库端 Take(MaxTaskRows + 1)，流式累计每组处理数量、耗时总和与超时数，不保留完整 TaskRow 列表。第 limit + 1 行触发明确异常，不返回不完整指标。保留原节点名称和排序规则，不额外引入业务语义改变。

积压同样在授权、状态和流程筛选后，数据库端 Take(MaxBacklogRows + 1)，流式按 Ordinal 分组。未超限时返回精确 Top 10；超限时整体请求失败。提示应说明积压独立于日期，建议按流程筛选或使用宿主报表，不能误导用户仅缩短时间范围。

新增 `ApprovalErrorCode.StatisticsLimitExceeded`（追加枚举值），沿用现有 ProblemDetails 默认 HTTP 400 映射；错误文本包含统计类别和预算值，不包含无权读取的数据。前端使用既有错误展示，必要时只修正文案。

不选择简单 SQL GROUP BY 的原因：四个 Provider 的大小写、重音、尾空格和 binary collation 行为不同。若未来必须对任意规模返回全量 Top 10，应另立数据库精确分组或预聚合方案并验证排序规则，而不是删除本次资源边界。

## 实施清单

### 1. 固定基线与回归用例

- [x] 检查执行时 HEAD、工作区和本会话之后的用户变更，不覆盖已有修改。
- [x] 阅读 karpathy-guidelines、aspnet-core；测试修改按 code-testing，测试命令按 run-tests；修改 Vue 时按 vue-best-practices。先检查仓库 skill overlay。
- [x] 记录当前默认值、公开 API 和四 Provider 统计断言。先添加可失败的针对性回归，不以旧报告代替执行。

### 2. 异步校验

- [x] 删除服务同步 Validate；搜索所有调用方、反射/公开 API 基线及文档引用，仅迁移对应服务入口。
- [x] 保留 ValidateAsync 的 DefinitionValidate 权限、错误码、CancellationToken 传播和 ValidateCore 扩展边界。
- [x] 用真正延迟完成的权限 Task 验证方法返回未完成 Task、授权完成前不调用校验扩展；拒绝、取消及权限超时仍受原边界处理。
- [x] 更新 `contracts/Approval.Core.api.txt` 和迁移说明；静态纯校验 API 不变。

### 3. 统一严格幂等及宿主接入

- [x] 删除 Core 的 RequireOperationId 开关及证书宿主配置读取。
- [x] 检查样例、rehearsal、独立消费者及前端写操作是否发送稳定键，不在每次重试时重新生成键。
- [x] 真实用户调用统一使用带键 executor；流程算法使用明确系统夹具，缺键与非法键回归仍检验生产门禁，不保留无键兼容测试。
- [x] 默认无键 HTTP 写返回 400/OperationIdRequired，且数据库无业务、审批、操作和 Outbox 残留；默认带键写入及相同键重放只产生一次已提交副作用。
- [x] 保留相同键不同内容冲突、权限撤销、系统能力和文件上传例外测试。
- [x] 更新 README、事务幂等和升级文档、CHANGELOG、OpenAPI 幂等说明，历史报告保持历史事实。

### 4. 统计资源边界

- [x] 增加统计选项、合法性检查及专用超限错误码；删除旧构造器，统一使用宿主 ApprovalOptions。
- [x] 实例查询增加 Id 次级排序；任务 join 确切的受限实例子查询，移除时间戳近似截断和加载后的内存 ID 过滤。
- [x] 任务查询使用数据库 Take(limit + 1) 和流式聚合，删除任务 ToListAsync 及二次列表复制。
- [x] 积压查询使用数据库 Take(limit + 1)，超限整体拒绝，保留原 Ordinal 分组、排序和 Top 10。
- [x] 验证所有查询先应用权限/QueryScope、业务过滤，再应用预算；无权/已结束/取消记录不消耗错误的匹配预算。
- [x] 修正文档与 API 描述，说明实例截断与任务/积压超限是不同情况。

### 5. 契约与最终审查

- [x] 用仓库生成器同步 C# API、HTTP/OpenAPI 和 TypeScript 契约，不手工修改生成文件。
- [x] 如更新监控界面，确认新请求失败时清理旧数据，不显示上一成功请求的指标冒充本次结果。
- [x] 检查 diff 没有迁移、框架/包升级、授权绕过或无关格式化。

## 验证矩阵

以下是验收要求；当前完成证据见 `docs/async-validation-idempotency-statistics.md` 的续作验证，文末前一轮记录保留为历史。

| Requirement | Evidence |
| --- | --- |
| 异步授权不阻塞、未授权不校验 | 新增延迟权限 Task、拒绝、取消用例；既有 PermissionTests.DefinitionValidate 相关用例继续通过 |
| 固定要求稳定键 | 使用真实 DI/HTTP 夹具；验证缺键拒绝及所有持久化表无副作用 |
| 缺省带键提交与重放 | HTTP/SDK 相同键同内容只推进一次，不同内容冲突，当前权限撤销仍拒绝 Replay |
| 非法键与边界 | 缺键/空白/超长/控制字符在委托前被拒绝，裸工作单元不能绕过预约键；系统能力与文件上传边界保持 |
| 任务查询只读取选中实例 | 同 CreatedAt、不同 Id、超过实例上限；检查未选实例任务不参与结果和读取预算 |
| 任务有界且指标不变 | 空集合、恰 limit、limit+1；大单实例、多任务状态、系统/重新提交排除、耗时平均与超时边界 |
| 积压有界且不误报全量 | 空集合、恰 limit、limit+1，超限专用错误；未超限精确 Top 10、相同计数排序 |
| 分组与筛选语义 | bob/BOB、重音/尾空格、用户/角色同名、nullable DueAt、DueAt==now、最早时间、流程与 QueryScope |
| 四 Provider 翻译与限制落库 | SQLite/MySQL/PostgreSQL/SQL Server 实际运行；记录 SQL 中受限子查询和任务/积压行限制，不能只断言结果数量 |
| 前端错误和契约 | 超限 ProblemDetails、生成契约一致、typecheck、监控旧数据不残留及独立消费者构建 |

在仓库根目录执行的完整验证入口：

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'MySql'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'SqlServer'
pwsh -NoProfile -File './eng/verify-databases.ps1' -Provider 'PostgreSql'
```

先按 run-tests 选择目标类运行新增用例，再运行以上门禁。真实数据库命令需要可用 Docker；若执行环境缺失，明确记录未运行及缺少条件，不引用历史 CI 作为新结果。本计划不授权推送、发布或操作用户数据库。

## 风险与回滚

- Validate 删除会使旧源码调用编译失败，迁移为 await ValidateAsync；不要保留阻塞授权的备用分支。
- 统一严格幂等会拒绝无键调用；仓库调用方使用带键入口，项目尚未发布，不保留关闭开关或无键重载。
- 统计超限从可能耗尽资源改为明确失败，属于刻意改变的行为；不同预算需由宿主根据压测配置。
- 新流式实现需要四 Provider 实际翻译验证；不能通过切换客户端求值规避翻译失败。
- 不改数据库结构，回滚无需数据迁移。审查本轮独立提交后可显式 revert；不执行 reset/clean，不自动关闭严格幂等或扩大统计预算。

## 执行会话交接

执行入口：按本计划完成实现、回归、契约和文档同步，并报告确切命令、测试结果及尚未执行的门禁。无需再次请求相同实现范围的确认。若用户另行指定全量大规模报表或要求保留同步 API，先更新相关设计决定，再实施。

## 前一轮执行结果（续作前历史）

三项代码及配套迁移说明已完成，详见 `docs/async-validation-idempotency-statistics.md` 的逐要求测试映射。

- Release solution：零警告、零错误；后端 Passed 637 / Failed 0 / Skipped 0（Core 439、HTTP 83、SQLite Database 56、Certificate 59）。
- 九项 Model Drift、ReferenceHost 27 checks、前端 typecheck、UI 181 + demo 6 测试、边界 7 测试、库与 demo 构建、契约检查通过。
- 四个 NuGet 包及 npm 包完成打包、独立消费者构建与运行，Core-only 消费者 18 checks 通过。
- 完整 verify 脚本第一次在浏览器阶段失败：旧协议校验用例没有幂等键，先被严格策略拒绝。补充稳定键后，仅重跑剩余浏览器阶段：5 passed / 0 failed / 0 skipped。未重复已通过的后端等阶段。
- 完整门禁日志 `output/async-statistics-verify.log` 保留初次浏览器失败；修复后的成功日志 `output/async-statistics-browser.log` 与同一消费者 `browser/results.json` / `passed.json` 是最终浏览器证据。不要将原始完整脚本的非零退出码描述成零。
- 三种真实服务数据库未运行：Docker CLI 可用，但 dockerDesktopLinuxEngine pipe 不存在。四 Provider 共用回归已编写，SQLite 已实际通过；未推送、发布或操作用户数据库。
