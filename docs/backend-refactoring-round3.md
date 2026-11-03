# 后端重构第三轮：Query 与 Forms 模块边界

2026-11-03：本地编译、业务回归和 Diff 审查完成。按用户追加约定，数据库专项、Provider 矩阵与完整 CI 门禁通过 GitHub CLI 运行，本地不执行、不启动 Docker。

## 查询职责与契约

- `ApprovalQueryService` 保留公共 Facade 和工作列表的共用筛选 / 排序 / 分页，新增一个内部 `Engine/Query/ApprovalInstanceReader` 承接详情、业务历史、公用定义、访问与投影，没有按每个入口拆 Handler。
- 敏感详情 / 业务历史返回 InstanceDetail，公开定义返回 ApprovalDefinitionSnapshot，公开 Query 返回值不包含 EF Entity。敏感权限、数据 Scope、查看策略、字符串比较和任务 / 日志顺序保持。
- 多版本业务历史一次预取定义 JSON，在逐实例访问检查通过后才解析；同 scope 重复查询复用既有缓存。权限结果仍逐次检查，不缓存 Allow。
- 数据装配集中，HTTP JSON 不变。C# Query 返回值及新增 snapshot overload 已写入 [迁移说明](backend-context-api-migration.md)；审查后同步公共 API 基线。
- TestHost 通过已有 DbContext 读取 Entity 图供算法测试查看 StateJson；真实权限用例仍直接调用公共 Query，未新增生产测试接口或测试写保护豁免。

## Forms 协作入口

新增 Core internal `ApprovalFormsBridge`，固定 Forms 元数据所有者、指定实例 Title、稳定 ID / 固定定义、宿主调用隔离与资源 Viewer。Forms 不再直接使用 UnitOfWork、WriteOwner、WriteAuthorization、HostServices、内部 Authorization 或 DefinitionAvailability。

friend assembly 保留，编译器访问仍宽；签名、字段、局部变量及 IL 成员引用的架构测试限制实际内部依赖。Bridge 不公开任意所有者选择，Forms 不能借公共 UnitOfWork 改 Core 状态。共享事务、附件补偿、草稿提交与字段权限保留。

## 验证

每个主题均独立 Release solution build，最后 0 warnings / 0 errors。

| 检查 | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Reader 提取相关测试 | 41 | 0 | 0 |
| Query / 访问 / 分页 / 新边界测试 | 44 | 0 | 0 |
| Forms / 字段 / 附件 / 草稿 / WriteGuard / 幂等 | 75 | 0 | 0 |
| 本地 Core 全量 | 391 | 0 | 0 |
| 本地 HTTP 全量 | 81 | 0 | 0 |
| 本地 Certificate 全量 | 58 | 0 | 0 |
| 本地业务测试合计 | 530 | 0 | 0 |

Contracts 检查成功；新增 Core / HTTP API 差异已逐项审查，HTTP JSON / OpenAPI / schema / TypeScript 无实际 Diff。数据库专项不包含在上述本地计数中，CI 结果按具体 run / head另行补录。

| Requirement | Evidence |
| --- | --- |
| 两个版本一次加载定义 JSON，重复读取复用缓存 | `QueryBoundaryTests.BusinessHistory_BatchesDefinitionReadsAcrossVersionsAndReusesTheRequestCache`，明确 SQL 计数为1 |
| 无权查看时不解析损坏定义 | `QueryBoundaryTests.DeniedHistory_DoesNotParseAnUnreadableCorruptDefinition` |
| 公开 Query 结果没有 EF Entity | `QueryBoundaryTests.PublicQueryResults_DoNotContainPersistenceEntities` |
| Forms 内部依赖范围有限 | `FormsBoundaryTests.Forms_ReferencesCoreInternalsOnlyThroughTheFixedBridge`，检查实际签名和 IL 引用 |

相关 TRX：`output/backend-refactoring/round3-query/`、`round3-forms/`、`round3-local/`。未新增依赖、程序集或模型变化。对宿主逐资源 ACL 的额外查询不做不安全合并，未宣称所有 N+1 或权限复查已消除。
