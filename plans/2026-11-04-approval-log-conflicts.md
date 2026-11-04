# 并发审批日志唯一约束异常修复

基线 cc4ff472f4d823805eae7a9b49e2d453a9e96bdd，指定分支继续执行。本轮用户明确要求完成实现、回归及提交；计划与执行在同一任务内完成。

## 范围与不变量
- 生产仅 ApprovalDatabaseErrors、ApprovalUnitOfWork.ExecuteOwnedAsync 的 Engine 命令异常边界、CommandExecutor 已转换 Conflict 的传播；Flush原有EF并发catch保留。
- 保留 Touch/ConcurrencyStamp/EF concurrency catch；日志唯一索引、事务/回滚/失败作用域保护、幂等规则不变。
- 不修改 Resolver、Governance、DI 架构、模型或 Migration，不新增依赖。
- 精确匹配当前模型日志表与 InstanceId/Sequence 唯一索引，结合 DbUpdateException 的受影响实体；其他唯一、FK、连接、SQL错误不归为日志冲突。

## 实施清单
- [x] 审查普通 Engine/UnitOfWork/CommandExecutor 路径，核对历史实际 Provider 异常、实体索引。
- [x] 保存边界增加精确识别及一次包装，InnerException 保留。
- [x] 两独立 scope/context，先完成旧任务读取与 CanHandle 校验，再显式同步释放真实 Approve 调用；验证恰一成功/一 Conflict。
- [x] 恢复原始陈旧审批竞争，保留独立 ConcurrencyStamp 清理竞争测试。
- [x] 验证业务记录、实例/任务、唯一日志序号、消息/Outbox、AfterCommit/失败作用域。
- [x] 各真实 Provider 检验实际日志唯一异常与无关唯一/FK/SQL/连接失败；记录代码/约束名/EF写命令顺序。
- [x] 本地 Release、Core、HTTP、Certificate；数据库全部通过 gh/Actions（包括 SQLite 完整回归）。
- [x] Contracts/九项 Model Drift/既有全量门禁，下载 TRX/manifest/head/hash/竞争证据核验。
- [x] 精确报告、提交指定分支，完成最终状态检查。

## 验证与回滚
SQLite 原生普通事务在 BEGIN 保留写锁，两个上下文在事务外先加载相同旧状态，gate 同时放行真实审批；SQLite 的 BEGIN 自然串行化，第二个依然保留旧 EF 跟踪图。其他 Provider 不假设 EF INSERT/UPDATE顺序，真实命令记录作为证据。
不使用 Task.Delay 猜测竞争，不自动重试隐藏 Conflict；等待都有超时上限。单次操作失败必须完成事务释放后再查询最终状态。
GitHub Actions 需要提交可拉取的代码：本地检查后创建验证提交并推送，所有远端验收完成后提交最终报告；不重写历史、不强推。若失败，在原分支追加最小修复并重新验收精确 SHA。
回滚仅由显式 revert 本轮提交完成；不改 Migration，不清理用户数据。
