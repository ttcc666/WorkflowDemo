# 宿主事务与命令幂等

审批数据和业务数据使用同一 ApprovalDbContext。一个操作中的实例状态、任务关闭/创建、追加轨迹、抄送、站内信、Outbox 与业务结束回调在同一事务提交；任何异常回滚，失败作用域不可重用。

## 宿主完整命令

通过 IApprovalCommandExecutor.ExecuteAsync 传入类型、资源范围、稳定 OperationId、请求内容、权限码和委托。协调器在新 DI/DbContext 作用域中重试完整委托，固定本次选中的定义版本；同键同内容返回原结果，同键不同内容 Conflict。HTTP 写接口支持 Idempotency-Key，UI 按逻辑操作保存原键。

业务回调不得自行 SaveChanges 或提交事务；原有本地扩展的回滚边界继续测试。裸外部事务和扩展重入写操作明确拒绝。数据库暂时失败、COMMIT 确认丢失时，协调器在独立上下文核对已提交记录；无法确认则返回 CommitOutcomeUnknown，客户端保留原键查询/重试。

结果保留期结束后保留永久防重标记；旧键返回 ReplayExpired，不把历史请求当新业务再次执行。重放仍检查当前读取权限，撤权后不得访问旧结果。

## 并发

实例 ConcurrencyStamp 防止重复审批、撤回/审批、超时/人工、并行分支同时完成时丢失更新。BusinessSlot 的唯一约束保证同一 BusinessType+BusinessKey 只有一个活跃流程，Running 和 Returned 都占用；结束才释放。没有 Redis 或分布式锁。

## Outbox

事件与审批同事务写入，提交后由单个 Worker 投递。没有持久抢占或租约，确认前仍为到期 Pending；取消/崩溃后可恢复。失败另开事务记录尝试与错误，按 BaseRetryDelay × 2^(n-1) 退避，上限 MaxRetryDelay，耗尽 MaxAttempts 标 Failed。人工重试保留消息 ID，后台投递中返回 Conflict，成功记录不可重新发送。

外部处理器必须以消息/事件 ID 幂等；至少一次不等于外部恰好一次。SQLite 使用 deferred transaction 避免在等待外部 I/O 时提前占写锁；事务成功/失败仍保持本地原子性。维护保留/游标是可选能力，默认关闭，不能在活跃流程上按年龄清理。
