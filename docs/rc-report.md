# Approval.Core RC 最终验收报告

日期：2026-11-03（Asia/Shanghai）。结论：**RC Ready**。

本轮清理、本机正式验证与 GitHub 完整 CI 均通过。三个真实服务数据库已补验，Windows 全量验证、独立包消费者及浏览器 smoke 也通过。未发现新的已证实 P0／P1 代码缺陷，可以进入发布候选与基础架构冻结阶段。

## 1. Git 与范围

- 分支：`codex/engineering-extensibility`。
- 修改前 HEAD：`43acecca8174ad29a1c3864c2753b8dbc132a2db`。
- RC 运行代码与 CI 提交 HEAD：`fb125f0dcab7d2f10b0e35ffc08b46c2f3a5b478`。验收报告补充为后续本地文档提交；最终精确 SHA 见 `output/rc-2026-11-03/git-final.json`。
- 开始时工作区干净，无用户原有未提交修改。后续用户明确批准推送 `fb125f0` 并运行 CI，已正常快进推送到当前分支；未合并、未改写 Git 历史。CI 结果补充文档未再次 Push。
- 用户最新确认：项目未正式发布，无外部消费者／生产历史数据。因此明确授权直接重命名构造参数、删除附件死状态及对应迁移／测试／文档。
- 无新的权限框架、拦截器、SQL Parser、UnitOfWork 或消息供应商依赖；RequireOperationId 默认 false 及 OperationId 协议保持。

## 2. 删除依据与结果

全局检查发现，ArchiveId／DeletionPending 当前生产源码只有实体声明与读取拒绝分支，没有当前业务设置路径。唯一非迁移赋值来自旧归档保留测试。旧 Archive／Maintenance／File Cleanup 服务与实体已删除，因此两字段不能作为当前安全语义继续保留。

删除字段、隐式 EF 映射、OpenAsync／Usable 的旧分支、Tenant 存储键兼容格式及旧归档保留测试。真实安全规则仍在：可信身份／权限、未绑定文件上传者、实例访问、隐藏字段、跨实例／草稿绑定及服务端元数据校验。

九套上下文重新生成当前 InitialApproval，移除旧维护表、相关外键／索引及空 EmbeddedEngineBoundary 迁移。保留 InitialApproval 的既有标识只便于仓库定位，不表示支持已应用旧基线的开发库升级。没有操作用户数据库；旧可丢弃开发库应由开发者重建。

逐文件清单如下，路径相对于仓库根目录 `D:/Study/CSharp/Workflow`。

| 文件 | 修改与原因 | 公开 API | 数据库结构 | 数据兼容影响 |
| --- | --- | --- | --- | --- |
| `CHANGELOG.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `README.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `contracts/Approval.Forms.api.txt` | 正式生成器同步删除字段／空迁移导出类型的 API 记录 | 记录已授权删除 | 否 | 无外部消费者 |
| `docs/database-validation.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `docs/persistence-write-boundaries.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `docs/rc-report.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `docs/round3-report.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `docs/transactions-and-idempotency.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `docs/upgrade-0.3.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `eng/Approval.RehearsalTool/Rehearsal.cs` | 旧 Tenant 名改为凭据分组；移除无作用 Scope 参数；令牌／行为保持 | 仅仓库工具 | 否 | 不宣称 Tenant 隔离 |
| `eng/Approval.RehearsalTool/RehearsalHost.cs` | 旧 Tenant 名改为凭据分组；移除无作用 Scope 参数；令牌／行为保持 | 仅仓库工具 | 否 | 不宣称 Tenant 隔离 |
| `eng/verify-databases.ps1` | 必需执行病例改为物理 schema／附件往返，零跳过检查保持 | 否 | 否 | 不允许假通过 |
| `eng/verify-reference-host.ps1` | 旧 Tenant 名改为凭据分组；移除无作用 Scope 参数；令牌／行为保持 | 仅仓库工具 | 否 | 不宣称 Tenant 隔离 |
| `plans/2026-11-03-rc-hardening.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `samples/Approval.Certificate/Infrastructure/Migrations/20261103051646_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.Certificate/Infrastructure/Migrations/20261103051646_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.Certificate/Infrastructure/Migrations/20261103065227_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.Certificate/Infrastructure/Migrations/20261103065227_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.Certificate/Infrastructure/Migrations/AppDbContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.Certificate/README.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `samples/Approval.Certificate/contracts/Approval.Certificate.api.txt` | 正式生成器同步删除字段／空迁移导出类型的 API 记录 | 记录已授权删除 | 否 | 无外部消费者 |
| `samples/Approval.ReferenceHost/Migrations/MySql/20261103051716_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/MySql/20261103051716_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/MySql/20261103065317_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/MySql/20261103065317_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/MySql/MySqlReferenceContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/PostgreSql/20261103051759_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/PostgreSql/20261103051759_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/PostgreSql/20261103065327_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/PostgreSql/20261103065327_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/PostgreSql/PostgreSqlReferenceContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/SqlServer/20261103051733_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/SqlServer/20261103051733_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/SqlServer/20261103065322_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/SqlServer/20261103065322_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/SqlServer/SqlServerReferenceContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/Sqlite/20261103051655_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/Sqlite/20261103051655_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/Migrations/Sqlite/20261103065312_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/Sqlite/20261103065312_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `samples/Approval.ReferenceHost/Migrations/Sqlite/SqliteReferenceContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `samples/Approval.ReferenceHost/README.md` | 更新当前状态／生产幂等指南／历史提示／执行与验收记录 | 否 | 否（记录基线变化） | 明确未发布前提 |
| `samples/Approval.ReferenceHost/smoke-test.ps1` | 旧 Tenant 名改为凭据分组；移除无作用 Scope 参数；令牌／行为保持 | 仅仓库工具 | 否 | 不宣称 Tenant 隔离 |
| `src/Approval.Core/ApprovalOptions.cs` | 严格模式生产建议注释，默认 false 不变 | 否 | 否 | 协议不变 |
| `src/Approval.Core/Background/BackgroundServices.cs` | tenants 直接改为 scopes；Factory 与生命周期不变 | 是：命名参数改变，用户授权 | 否 | 不保留外部兼容桥 |
| `src/Approval.Core/Engine/ApprovalEngine.cs` | 旧租户注释改为宿主语义 | 否 | 否 | 无运行变化 |
| `src/Approval.Forms/Attachments/ApprovalFile.cs` | 删除无业务写入者的 ArchiveId／DeletionPending | 是：两项属性删除，用户授权 | 是：隐式映射删除 | 无生产旧状态 |
| `src/Approval.Forms/Attachments/ApprovalFileService.cs` | 删除旧访问判断，保留真实授权和附件引用校验 | 否 | 否 | 当前行为回归通过 |
| `src/Approval.Forms/Attachments/IApprovalFileStore.cs` | 删除 Tenant 键格式兼容；保留年月随机键和路径校验 | 否 | 否 | 无生产旧存储键 |
| `tests/Approval.AspNetCore.Tests/ReferenceHostTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Approval.Core.Tests/FileTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Approval.Database.Tests/AtomicDatabaseContractTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Approval.Database.Tests/DatabaseContractTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Approval.Database.Tests/EmbeddedBoundaryMigrationTests.cs` | 删除旧归档保留测试，以 SchemaBoundaryTests 替代 | 只改测试 | 验证目标修正 | 无生产历史归档 |
| `tests/Approval.Database.Tests/FinalAcceptanceDatabaseTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Approval.Database.Tests/GovernanceDatabaseContractTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Approval.Database.Tests/Migrations/MySql/20261103051724_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/MySql/20261103051724_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/MySql/20261103065337_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/MySql/20261103065337_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/MySql/MySqlContractContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/PostgreSql/20261103051814_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/PostgreSql/20261103051814_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/PostgreSql/20261103065347_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/PostgreSql/20261103065347_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/PostgreSql/PostgreSqlContractContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/SqlServer/20261103051745_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/SqlServer/20261103051745_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/SqlServer/20261103065342_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/SqlServer/20261103065342_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/SqlServer/SqlServerContractContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/Sqlite/20261103051707_InitialApproval.Designer.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/Sqlite/20261103051707_InitialApproval.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/Migrations/Sqlite/20261103065333_EmbeddedEngineBoundary.Designer.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/Sqlite/20261103065333_EmbeddedEngineBoundary.cs` | 删除空边界迁移及其旧模型，清理未发布维护体系 | 仅示例迁移类型删除 | 删除旧维护基线 | 无生产数据；旧开发库自行重建 |
| `tests/Approval.Database.Tests/Migrations/Sqlite/SqliteContractContextModelSnapshot.cs` | 用当前 EF 模型重生成 InitialApproval／snapshot，删除旧维护表、列、关系 | 否 | 是：未发布基线 | 无历史兼容桥 |
| `tests/Approval.Database.Tests/ReferenceHostDatabaseTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Approval.Database.Tests/SchemaBoundaryTests.cs` | 新增物理死表／列缺席及当前附件元数据往返测试 | 只改测试 | 验证当前基线 | 替换虚构历史要求 |
| `tests/Certificate.Api.Tests/CertificateMigrationTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/Certificate.Api.Tests/ModulePackagingTests.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `tests/shared/ReferenceHostScenario.cs` | 清理未使用 Tenant 参数／误导命名，或核验唯一当前迁移；断言保持 | 只改测试／夹具 | 否 | 核验真实单宿主行为 |
| `web/packages/approval-ui/src/features/approval/ApprovalActions.test.ts` | 真实 Element Plus／SSR 注入替代 stub，保留行为断言并核验零 warning | 只改测试 | 否 | 生产组件不变 |

## 3. 安全与架构审查

| 检查 | 代码依据／结果 | 实测证据 |
| --- | --- | --- |
| 核心写保护 | ApprovalWriteAuthorization 覆盖 Task／Instance／Log／Operation／Outbox／BusinessSlot／Definition／Management／Audit；SaveChanges 检查来源、只追加历史、保存后再次核验；真实 EF bulk 方法按目标拦截 | HostTrackedWrites_AreRejected_AndDatabaseRemainsUnchanged、HostBulkWrites_AreRejected_BeforeExecutingSql、HostInterceptorAfterSaveGuard_CannotIntroduceApprovalChanges |
| Raw SQL | 当前 EF 表名／schema 的普通 DML 目标检查，覆盖同步／异步 nonquery、reader、scalar | HostSqlDml_IsRejected_InEveryExecutionPath、BusinessRawSql_MayMentionAndReadApprovalTables |
| 权限作用域 | Engine／Forms／Operations／Outbox 的内部写权限分离；Extension 使用 None、禁止重入／事务提交，Host Services、Resolver、数据／结果 getter 隔离 | PublicDataEditCallback_CannotChangeTaskStateOrPersistedHistory、HostCannotModifyFormsData_WhileNormalFormsServicesWork、ResultGetter_CannotInheritOperationWriteAuthority、InputGetter_CannotInheritEngineWriteAuthority |
| 事务与租期 | 失败作用域作废；裸外部事务拒绝；池化租期清空失败与权限记录；宿主业务写入保持 | PublicUnitOfWork_DoesNotAuthorizeHostApprovalChanges、PooledHostContext_NewLeaseDoesNotInheritFailedTransactionOrWriteAuthority、ExistingHostContext_PreservesBusinessSaveApprovalTransactionConcurrencyAndOutbox |
| OperationId | actor／kind／scope／key 构成范围，唯一索引先预约，摘要／版本校验，Replay 复核当前权限；未知提交在新 scope 核验；批量分别记录计划／实例结果／汇总 | KeyedCoreStart_ReplaysOriginalVersionAndRejectsDifferentContent、KeyedBulkReassign_ReplaysOriginalResult_AndResumesPartialCompletion、OperationEndpointTests；服务数据库并发／提交核验病例在三个真实 Provider 的本轮 CI 全部通过 |
| Outbox | Worker、Dispatcher 与 ProcessDueAsync 检查关闭选项；关闭时保存 Pending，不解析／投递 Handler。ApprovalHostValidation 启用时解析真实 Handler，启动不发送探测事件 | EnabledDispatcherWithoutHandler_FailsApplicationStartup、RegisteredHandlerThatCannotBeConstructed_FailsStartup、ExplicitDispatcherConfiguration_PreservesOutbox_AndDeliversOnlyWhenEnabled：全量通过 |
| 身份／权限 | 缺身份拒绝；未注册人工权限默认拒绝；异常／超时返回 AuthorizationUnavailable；系统操作必须命中显式能力 | ActorSpoofAndMissingIdentity_AreRejectedBeforeProvider、PermissionFailureOrTimeout_FailsClosed、MissingPermissionProvider_RejectsEvenAnAdministratorBeforeCreatingData |
| 模块方向 | Core 无 ASP.NET Core／Forms／UI 引用；Forms → Core，Forms HTTP → Forms／ASP.NET Core；通知只携带宿主路由，Outbox 无供应商；业务实体不受审批所有权保护 | 边界 7 测试、契约生成、18 项 Core-only 真实包消费检查 |

上述证据来自本轮本机运行及 GitHub Run 41635117047，CI headSha 已核验为 fb125f0；未引用旧 CI 结果。没有新增需修复的已证实 P0／P1；没有通过跳过、吞异常或降低断言获得通过。

**已知应用层边界**：ApprovalSqlWriteGuard 不是数据库安全隔离，也不是完整 SQL 解释器。动态 SQL、存储过程、视图／同义词、触发器和复杂间接写入不提供完整保证；直接 GetDbConnection、独立连接、移除拦截器、迁移／数据库管理权限属于可信宿主边界。持有数据库写权限的宿主仍能直接修改数据库。

## 4. 实际验证

工作目录：`D:/Study/CSharp/Workflow`；SDK 10.0.401、EF Core 10.0.14。主入口 `pwsh -NoProfile -File 'eng/verify.ps1'` 实际退出码 **0**，结尾 `Verification succeeded: All`。

| 项目 | 实际命令（主脚本内部命令亦逐项记录） | Passed | Failed | Skipped | 结果 |
| --- | --- | ---: | ---: | ---: | --- |
| Approval.Core.Tests | `dotnet test 'Workflow.slnx' --no-build --no-restore --configuration 'Release' --verbosity 'minimal'` | 378 | 0 | 0 | 通过 |
| Approval.Database.Tests／SQLite | 同一 solution test 命令 | 31 | 0 | 0 | 通过，包含新物理 schema 检查 |
| Approval.AspNetCore.Tests | 同一 solution test 命令 | 81 | 0 | 0 | 通过 |
| Certificate.Api.Tests | 同一 solution test 命令 | 58 | 0 | 0 | 通过 |
| Windows 本机完整验证 | `pwsh -NoProfile -File 'eng/verify.ps1'` | — | — | — | 通过；不是远程 CI Run |
| Windows 远程 CI | `eng/verify.ps1`；GitHub Run **41635117047**、job verify | 完整验证通过 | 0 | 0 | 本轮 SHA fb125f0；548 项 .NET、UI 180、Demo 6、边界 7、模型 9、浏览器 5及包消费者通过 |
| PostgreSQL | CI：`./eng/verify-databases.ps1 -Provider 'PostgreSql'` | 31 | 0 | 0 | 通过；PostgreSQL 17.11，Run 41635117047 |
| SQL Server | CI：`./eng/verify-databases.ps1 -Provider 'SqlServer'` | 31 | 0 | 0 | 通过；SQL Server 2022（16.00.4255），Run 41635117047 |
| MySQL | CI：`./eng/verify-databases.ps1 -Provider 'MySql'` | 31 | 0 | 0 | 通过；MySQL 8.4.10，Run 41635117047 |
| EF Model Check | 下列九条模型命令 | 9 检查 | 0 | 0 | 均无 pending model changes |
| ReferenceHost | `pwsh -NoProfile -File 'eng/verify-reference-host.ps1' -SkipBuild`，及复制后的 NuGet 消费宿主同一脚本 | 27 + 27 检查 | 0 | 0 | 两次本轮运行通过 |
| UI Tests | web 下 `pnpm test` → @approval/ui vitest run | 180 | 0 | 0 | 19 文件通过 |
| Demo Tests | 同一 pnpm test → approval-demo vitest run | 6 | 0 | 0 | 2 文件通过 |
| Boundary Tests | web 下 `pnpm check:boundaries`、`pnpm test:boundaries` | 7 | 0 | 0 | 通过 |
| Contracts | web 下 `pnpm contracts:check --no-build`；本轮先运行 `pnpm contracts:update` | 全部基线匹配 | 0 | 0 | 只移除已授权 Forms 属性及示例空迁移类型；HTTP／JSON schema 无漂移 |
| NuGet Pack | 四个 src 项目分别 `dotnet pack '<project>' --configuration 'Release' --no-build --output 'output/packages/0.3.0'` | 4 包 | 0 | 0 | 成功，并以当前包摘要验证真实消费者 |
| npm Consumer Build | `pwsh -NoProfile -File 'eng/verify-packages.ps1' -SkipBuild -RunBrowser` 内 pnpm pack、独立目录 install／build | 1 包／独立构建 | 0 | 0 | 通过，不引用框架源码 |
| Core-only Consumer | 包脚本中的独立 EmbeddedHost migration／run | 18 检查 | 0 | 0 | 真实 NuGet 包／IdentityDbContext／无 Forms 程序集 |
| Browser Package Smoke | web 下 `node 'scripts/browser-smoke.mjs' --manifest '<本轮 verification.json>'` | 5 | 0 | 0 | results.json expected=5、unexpected=0、flaky=0、skipped=0 |

九套 EF 检查的精确命令：

```powershell
dotnet ef migrations has-pending-model-changes --project 'samples/Approval.Certificate' --startup-project 'samples/Certificate.Api' --configuration 'Release' --no-build
foreach ($provider in @('Sqlite', 'MySql', 'SqlServer', 'PostgreSql')) {
    dotnet ef migrations has-pending-model-changes --project 'samples/Approval.ReferenceHost' --context ($provider + 'ReferenceContext') --configuration 'Release' --no-build
    dotnet ef migrations has-pending-model-changes --project 'tests/Approval.Database.Tests' --context ($provider + 'ContractContext') --configuration 'Release' --no-build
}
```

### 修改需求与直接证据

| Requirement | Evidence |
| --- | --- |
| 删除真实数据库中的死表／字段并保留附件元数据 | InitialMigration_CreatesCurrentSchemaAndRoundTripsAttachmentMetadata：SQLite 及 CI PostgreSQL／SQL Server／MySQL 全部通过 |
| 保持当前附件访问与引用安全 | FileTests.下载权限_未绑定只有上传者_绑定后按实例权限_隐藏字段不可下载；他人的文件_已用于其他申请的文件_类型不符的文件不能提交：Core 全量通过 |
| 当前唯一迁移与往返 | Migrations_RollBackAndReapply_WithoutModelDrift；Initial_migration_creates_current_schema_and_repeated_initialization_preserves_rows；Initial_migration_can_roll_back_empty_schema_and_recreate_without_model_drift；Module_contains_only_current_initial_migration_and_model：通过 |
| 使用真实 Element Plus，原权限断言保持且无 warning | 审批操作的权限能力／只显示单独允许的动作，未提供能力时拒绝显示；允许退回不代表允许驳回，节点禁止退回时仍不显示：定向 2 项及全量 UI 180 项通过 |
| 清理误导性的 Tenant 隔离断言 | ReferenceHost_BackgroundScopeRecoversAfterFailedBatch、ReferenceHost_SystemPermissionsRequireKnownWorkerAndAllowedCapability、Governance_DisabledFlowKeepsOriginalInstanceAndOtherDefinitionDraftIndependent、FinalAcceptance_BulkReassignmentReportsPerInstancePermissionAndKeepsDeniedInstances：本机全量通过 |

### 本机环境失败记录（已由 GitHub CI 补验解除阻塞）

- 本机 PostgreSQL／MySQL：Docker Hub registry HTTPS 连接超时；SQL Server MCR pull 返回 unexpected EOF。之后镜像源尝试仍保留相同 digest，但 Docker Linux 引擎 pipe 已不可用，本机服务数据库测试未运行。
- 本机 PostgreSQL 需要凭据；SQL Express 实际版本 9.00.3077.00（SQL Server 2005），不能替代当前 Provider 支持环境。只执行了只读连接探测，没有创建／删除用户库，没有使用已有其他数据库环境的连接进行写入，也没有修改连接配置。
- 本机数据库入口 manifest 均 validated=false、testExitCode=null，没有 TRX。此处仅记录失败尝试；后续 GitHub 三个数据库的 manifest 均 validated=true、testExitCode=0，TRX 均 31／0／0。
- 本轮日志：`output/rc-2026-11-03/verify.log`、`ui-targeted.log`、`contracts-update.log`、`migration-generation.log`、`database-<Provider>.log`。
- 数据库失败 run ID：PostgreSql `d6e5326d7fee4f0983c9bdf684850c0b`；SqlServer `cbb5cff9f77a41e18c4cc087a1efc2fd`；MySql `83d249810e254d07a9676e66959f2930`。这是本机脚本 ID，**不是 CI Run ID**。
- 本轮独立消费者：`output/engineering/consumer-217ec23a75844faca315821decd12663`，含 verification.json、embedded-host/verification.json、browser/results.json、browser/passed.json 与审批截图。
- 包 manifest 的 Commit 是执行时基线 HEAD；工作树包含本轮修改，其源文件指纹 `448a65426607b6089eb11f065dee0f5832b95494b9113d373e1cc73d75a7724a`。不将该字段解释为旧提交原样验证。验收后新增报告／更新执行清单仅为文档变化，不改变已验证运行代码或包字节。

### GitHub CI 完整证据

- [Run 41635117047](https://github.com/ttcc666/WorkflowDemo/actions/runs/41635117047)，事件 push，headSha=`fb125f0dcab7d2f10b0e35ffc08b46c2f3a5b478`；四个 job 均 success。
- MySql job 125914242127：本机下载 TRX Passed=31、Failed=0、Skipped=0；脚本 runId `a79bb75d1c52448fa6c8d0780b3d302d`。
- SqlServer job 125914242303：TRX 31／0／0；脚本 runId `77c3c393af92471e8988e7b11b7e14c8`。
- PostgreSql job 125914242014：TRX 31／0／0；脚本 runId `373479bd5de84a6b917768d5f85c5878`。
- Windows verify job 125914242062：四个 .NET 项目 378／31／81／58，均零失败／跳过；UI 180、Demo 6、边界 7；九个 EF model check、两个 27 项 ReferenceHost smoke、四个 NuGet 包及 npm 消费者、18 项 Core-only 检查和 5 个浏览器测试通过，无 Vue warning。
- 数据库 artifact 的 head、provider、validated、testExitCode 均核验；文件 SHA-256 与 manifest 一致，TRX 非空且所有病例成功，四项必需迁移／schema／事务／写保护病例实际执行。Linux 锁文件 SHA-256 为 `E7062ABD306D1C9F24EE54F2D61E4403EF6C68D8EF7765CE1F0CF4AEE5AABC1C`，与同一源文件 LF 字节一致；Windows 工作树的 CRLF 差异单独确认。
- Windows 下载产物核验：release-manifest Commit=fb125f0，5 个包；browser stats expected=5、unexpected=0、skipped=0、flaky=0；Core-only 18 检查；两个 ReferenceHost smoke 均 27 检查。
- CI 源文件指纹 `5c644cd68e563291762d5bda7a6273e77d2fa3ee23a7510dd3786762d1bc9eab`，由已提交源码生成。验收后只更新报告和计划，没有改变已验证运行代码。
- 全部证据保存于 `output/rc-2026-11-03/github-ci/`：ci.log、run.json、database-evidence.json、windows-evidence.json、三个数据库 artifact 与 Windows artifact。

```powershell
gh run view '41635117047' --repo 'ttcc666/WorkflowDemo'
gh run view '41635117047' --repo 'ttcc666/WorkflowDemo' --log
```

## 5. 剩余债务与影响

| 项目 | 阻碍发布 | 安全／兼容影响 | 后续处理条件 |
| --- | --- | --- | --- |
| 前端 Forms 与 UI 仍同一 npm 包 | 否 | 后端 Forms 已独立，Core-only 消费通过；前端拆包会涉及契约 | 有真实独立前端消费需求时再拆分 |
| FieldPermissions／FieldAccess／HiddenFields／Channels | 否 | 当前真实表单权限、数据投影与通知路由仍使用；不是附件死状态 | 真实模块契约需求出现后定向调整，不在 RC 扩大重构 |
| Operation 版本／IsTombstone 分支 | 否 | 当前结果／防重协议表面保留，本轮按原任务不改 OperationId 协议；不声称存在生产旧数据 | 独立审查操作结果协议时决定是否删除未使用版本分支 |
| Vite >500 kB chunk、Node FORCE_COLOR／NO_COLOR 提示 | 否 | 构建／5 个浏览器测试通过，无 Vue 组件 warning；主要是加载体积与运行器环境提示 | 真实性能指标或 CI 环境需要时处理，不调高阈值掩盖提示 |

## 6. 结论与下一步

**RC Ready**：本轮代码清理、身份与权限、写保护、事务／幂等、四种数据库、九套模型检查、Windows CI、独立包消费者与浏览器全部通过。原本机环境限制已由对应提交的真实 CI 补足证据，不再阻碍 RC 验收。可以进入基础架构冻结期；后续由真实集成需求与已确认缺陷驱动。

```powershell
Set-Location -LiteralPath 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File 'eng/verify.ps1'
pwsh -NoProfile -File 'eng/verify-databases.ps1' -Provider 'PostgreSql'
pwsh -NoProfile -File 'eng/verify-databases.ps1' -Provider 'SqlServer'
pwsh -NoProfile -File 'eng/verify-databases.ps1' -Provider 'MySql'
```
