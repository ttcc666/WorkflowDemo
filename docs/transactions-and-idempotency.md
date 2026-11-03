# 事务、幂等与恢复

ApprovalUnitOfWork 使用宿主注册的同一个 DbContext。本地业务、审批实例/任务、追加审计、操作记录和 Outbox 统一提交。任一操作失败会回滚事务并使该 scope 作废；宿主必须释放 scope，不能继续保存残留实体图。

命令包含 Kind、Scope、OperationId 和 Payload。执行器按操作人/命令类型/范围/键生成 LookupHash，保存 RequestHash、版本和结果 JSON。唯一索引在执行宿主委托前预约键，防止并发重复请求都执行成功。相同键与不同内容返回 Conflict；相同内容恢复原结果及绑定版本，并重新检查当前权限。

数据库 COMMIT 之后发生异常时，用新上下文及独立超时核验记录。无法确认时返回 CommitOutcomeUnknown，客户端保留原键重试。不能把提交未知当成确定失败并换新键。完整委托由数据库执行策略重试，每次使用新 scope。

OperationIdRequired 严格模式应在生产接入时启用。默认兼容无键调用不提供请求级重放保障；业务唯一占用仍由数据库约束保护。ResultJson 与 LookupHash 保留，因为分别用于提交恢复和避免数据库字符串排序规则破坏键身份。

严格模式核验的是命令执行器已在事务中预约带键操作的来源，单纯 `IApprovalUnitOfWork.IsActive` 不足以替代 OperationId。直接应用服务调用同样受检查；宿主业务写入和多个引擎调用放在同一个带键 executor 委托中。系统操作仍只接受显式允许的能力。

批量离职改派新增保留原签名的 OperationId 重载，并支持 HTTP Idempotency-Key。执行器分别保存候选清单、每个实例的结果和最终汇总；每个实例仍独立提交，部分完成后用原键恢复，不重新查询已改派后的候选来返回空结果。actor/kind/scope/key 定义键范围，相同范围/键不同内容冲突；当前权限撤销后仍不能读取旧结果。

草稿、表单提交/编辑、阅读状态、人工 Outbox Retry 和定义治理使用命令规则。只读/定义校验不要求键。文件上传的 Stream/FileStore 仍是明确的非请求幂等例外，保留存储失败补偿，不宣称数据库事务能回滚外部存储或实现跨系统 exactly-once。

没有操作过期、自动结果擦除或后台保留调度。IsTombstone 仅兼容旧版本已擦除的结果，新引擎不产生标记；此类旧键仍拒绝重执行业务，返回旧结果不可恢复的明确错误。

Outbox 是至少一次。数据库事务保证本地一致性，外部邮件/消息/推送等副作用由宿主基于 Event.Id 去重；不承诺跨系统 exactly-once。
