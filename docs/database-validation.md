# 数据库验证

支持 SQLite、SQL Server、PostgreSQL、MySQL。审批模型使用 UTC 转换、唯一键、外键和显式并发令牌；不同 provider 拥有各自宿主 Migration 链，禁止复用其他 provider 的 snapshot。

根目录运行 dotnet test Workflow.slnx --configuration Release；默认 Database.Tests 使用独立 SQLite 临时文件。服务数据库分别运行 pwsh -NoProfile -File ./eng/verify-databases.ps1 -Provider SqlServer（或 PostgreSql / MySql）。脚本只创建随机 approval_test_ 数据库；使用已有测试库必须显式指定独立测试库连接。

测试覆盖模型/迁移往返、Unicode 与 UTC、唯一约束、审批与业务原子提交、并发重复请求、未知 COMMIT 恢复、Outbox 重试，以及 EmbeddedBoundaryMigration_PreservesLegacyArchiveAndAttachmentData。

无容器或数据库服务时测试明确失败，不跳过、不把模型生成当成真实数据库通过。本轮实际结果与环境限制见 round2-report.md；历史报告不是当前证明。
