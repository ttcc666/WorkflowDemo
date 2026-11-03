# 后端重构第二轮：扩展上下文与 EF Entity 解耦

2026-11-03 用户明确授权 C# breaking change。代码迁移、本地后端、契约、独立包消费者及浏览器验证已完成；三个服务数据库的当前真实验证仍需补足。

## 实际改动与兼容性

- `ApprovalSnapshots.cs` 提供共用 Instance / Node / Task / Log 快照、StartPreparation 和 DataProjectionContext；映射集中在 Core 内部，避免为四种回调重复实例字段。
- business handler、resolver、lifecycle、AccessPolicy、projection、公开编辑委托都不接收 EF Entity。IApprovalResourceResolver 保持 DTO；IApprovalQueryScope 保留 SQL 范围组合能力。
- lifecycle 返回完整标题 / JSON，由 Engine 在原事务应用；编辑返回 ReplaceTitle / Title，Forms 不再从编辑回调直接改 Core 标题。Outbox allowSave 特例和写保护保持。
- InstanceDetail 与 HTTP task adapter 改为快照类型；FieldVisibility 使用 record with 过滤审计，保留 TaskId / Sequence / 时间，减少重复赋值。
- 已同步迁移 Certificate 业务回调、Core / HTTP / Database 测试 fixtures 和 override。没有新程序集、NuGet 依赖或平行旧接口。
- C# 签名变化列于 [API 迁移说明](backend-context-api-migration.md)。审查后更新 Core / AspNetCore / Forms / Certificate 四份 `.api.txt` 基线；HTTP OpenAPI、JSON schema、TypeScript contracts 没有实际 Diff。

## 验证

| 检查 | 实际结果 |
| --- | --- |
| 每个子主题独立 Release build | 最后均 0 warnings / 0 errors；迁移中缺失 using / readonly fixture 问题已修复 |
| 业务 / resolver / policy 相关用例 | Passed 77, Failed 0, Skipped 0 |
| lifecycle 相关用例 | Passed 12, Failed 0, Skipped 0 |
| Forms / projection / snapshot / WriteGuard | Passed 74, Failed 0, Skipped 0 |
| HTTP projection / forms / access / completion | Passed 25, Failed 0, Skipped 0 |
| 完整后端 | Passed 557, Failed 0, Skipped 0；Core387、HTTP81、Database31、Certificate58 |
| Migration 模型检查 | 九个上下文无漂移 |
| ReferenceHost SQLite smoke | 27 checks；`output/reference-host/3f7cfa2cdc8b4b4699b7658705f4c3ee/smoke.json` |
| C# / JSON / OpenAPI / TS 契约 | `pnpm contracts:check --no-build` 成功 |
| 独立 NuGet / npm 消费者 | `eng/verify.ps1 -Scope Packages` 退出码0；Core-only、Certificate、ReferenceHost 实际构建运行并核验包字节 |
| 包浏览器验证 | 5 passed，包括发布审批、拒绝匿名、诊断、Forms 退回重新提交、Definition 分类 |

消费者证据：`output/engineering/consumer-c5dbb74479044b7197c46be46969faec/verification.json`。Core-only 的十八项检查包括共享事务 / Save override、宿主状态改写拒绝、幂等、加签退回重新提交、并行汇聚、稳定事件 ID 与未加载 Forms。

| Requirement | Evidence |
| --- | --- |
| 回调输入不随实体推进而变化，编辑流程副本隔离 | `ExtensionContextTests.ResolverAndEditSnapshots_RemainStableWhenTheInstanceCompletes` |
| 开始准备支持标题清空与数据替换 | `ExtensionContextTests.LifecyclePreparation_CanClearTitleAndReplaceDataWithoutReceivingAnEntity` |
| 契约不携带 Entity / 运行态 / 并发字段 | `ExtensionContextTests.ExtensionContracts_DoNotExposePersistenceEntitiesOrRuntimeState` |
| 通过注入 DB 的非法任务写仍被拒绝 | `ApprovalWriteProtectionTests.PublicDataEditCallback_CannotChangeTaskStateOrPersistedHistory` |
| JSON v1 与独立响应副本 | `HttpResponseContractTests.Instance_response_preserves_v1_json_and_detaches_snapshots`（null / nonnull） |
| Forms 编辑标题、校验、审计仍一致 | `FieldPermissionTests.同意时修改可编辑字段_按新值重新计算与流转_标题更新_轨迹记录差异` |

## 真实服务数据库限制

已启动原先停止的 Docker Desktop，并成功连通 29.4.2 引擎。PostgreSQL 与 MySQL 在 pull 固定 digest 时无法连接 registry-1.docker.io:443，因此没有执行测试；manifest `validated=false`、`testExitCode=null`。SQL Server 当前下载结果另行补录。不能把 SQLite、模型或历史 CI 当作本次三种服务数据库通过。

- PostgreSql：`output/database-tests/PostgreSql/a12da5b88be44ebc8eab25db462745d4/manifest.json`。
- MySql：`output/database-tests/MySql/375e31b1f85c4dd59d4e0f6da7e36f88/manifest.json`。

## 复验

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Backend' -SkipRestore
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Contracts'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Packages'
```

第三轮继续查询 / Forms 边界，不把本轮已经移除的 Entity 契约重新包装回公开接口。源图中的持久化实体和必要数据库范围组合继续存在；本轮不声称所有公开数据库集成 API 均已消除 Entity。
