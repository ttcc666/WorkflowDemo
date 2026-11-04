# 后端重构第五轮：Definition、重复代码与性能

> 历史记录：本文保留该轮次当时的实现和验证事实。旧构造器、消息/幂等兼容、JSON 缺省版本及初始化迁移的当前状态，以[开发期兼容清理报告](unreleased-compatibility-cleanup.md)为准；本文不构成保留旧软件接口的要求。

本轮完成有实际代码依据的局部优化，没有增加程序集、NuGet、Repository 或 Handler 层。所有主题都独立编译 / 测试 / 审查 Diff；数据库专项继续由 GitHub CI 完成。

## 实际变化

- Definition 治理列表批量投影摘要字段和 HasDraftChanges，去掉逐 Key 加载完整 Management / DraftJson 的 N+1；仍执行原逐资源读取检查及三个 capability 判断。
- 新增内部 LatestPublished 查询条件供两个 Definition 列表复用；发起的 ActiveDefinitionId 规则保持独立。Statistics 的最新名称逻辑涉及另一套版本 / 排序语义，没有误合并。
- Metadata 每次只排序 / 组装一次 ApproverTypes；发布校验移除多余的第二次 ToList；删除未使用的 DefinitionService._options 字段和无用途对象分配。
- 仓库内剩余应用服务同步校验调用改为 ValidateAsync，保留原公开 Validate 兼容入口及权限检查。
- Forms 人员字段收集、去重后一次查询目录，再按原字段和原顺序组装缺失用户错误；空值和其它校验失败路径保持。
- Backlog 保留原投影查询，逐行流式汇总到大小写敏感的字典，不保留完整任务行集合；保留 Count、Overdue、Oldest 和前十排序。

## 验证证据

每次 Release solution build 最后 0 warnings / 0 errors。Definition 相关 49 passed；治理 / 权限 39 passed；Forms / 统计 / 通知等 51 passed，均0失败跳过。

| Requirement | Evidence |
| --- | --- |
| 三份治理摘要只读一次 Management，权限次数不变 | `DefinitionQueryOptimizationTests.GovernanceSummary_LoadsOneProjectionAndPreservesPerResourcePermissionChecks`：SQL1、权限16、每资源两次读取检查及三个 capability |
| 人员字段批量读取仍定位原字段错误 | `RuntimeQueryOptimizationTests.UserFields_ShareOneDirectoryReadAndKeepErrorsOnTheOriginalField`：valid / missing 两例，目录1、缺失用户仅 second 字段 |
| 流式待办汇总保留大小写、数量、时限和最早时间 | `RuntimeQueryOptimizationTests.Backlog_StreamsOrdinalAssigneesAndPreservesCountsDeadlinesAndOldestArrival`：bob与BOB不同组，Count2/1、Overdue2/0、具体最早UTC时间 |

最终本地业务测试 Passed 535, Failed 0, Skipped 0（Core396、HTTP81、Certificate58）；Contracts 成功，HTTP JSON / schema / TS 没有变化。

最终代码 `f6342b91569ed3ac66f01a42ea7ac09903e55108` 的 [GitHub CI41674494343](https://github.com/ttcc666/WorkflowDemo/actions/runs/41674494343) 全部success。MySql / PostgreSql / SqlServer各33passed / 0failed / 0skipped，manifest.head与artifact SHA256已核验；Windows全量568passed / 0failed / 0skipped，Contracts / Packages / Core-only18checks / Browser5passed。详见 [最终报告](backend-refactoring-final-report.md)。后续文档提交只补验收记录。

新增 `tests/Approval.Database.Tests/QueryOptimizationDatabaseTests.cs` 的两项 Provider 回归：`GovernanceSummary_TranslatesNullableDraftChangesAndFiltersResourcesOnEachProvider` 验证无草稿、未发布、版本相等 / 不等及逐资源 / 分类过滤；`Backlog_StreamsCaseDistinctAssigneesAndFiltersTasksOnEachProvider` 验证 bob / BOB、数量、nullable 时限、最早时间，以及已结束实例 / 已取消任务过滤。显式 Release build 已通过，数据库测试只交 GitHub 执行。

## 保留 / 未采取的方案

- 原公开同步 Validate 仍有 sync-over-async，用于源兼容，仓库内调用已全部异步。不能为了删除它而取消权限检查。
- LastByKey 的不区分大小写最后注册覆盖行为有文档 / 测试保证，未改成启动时全部拒绝；若要区分禁止重复与允许替换，需要独立配置行为契约。
- 未直接使用 SQL GROUP BY 代替 C# 的 Ordinal 审批人分组：宿主数据库排序规则可忽略大小写，直接替换会合并 bob / BOB。流式方式降低保留内存，保持四 Provider 语义；没有宣称减少数据库行数 / 往返。
- 不建立全局 JSON / Flow 缓存，避免生命周期、重新提交和审批编辑更新后的旧数据。现有操作 / 请求内缓存保留。
- Definition 的治理写路径已具备 Revision、审计、明确保存边界，本轮不为了类数量机械拆 Reader / Validator / Governance。

测试报表位于 `output/backend-refactoring/round5-definition`、`round5-runtime`、`final-local`。没有生产规模压测，不给出未经测量的延迟或吞吐提升百分比。
