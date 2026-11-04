> 历史记录：此文件描述前轮开发快照，不是 0.3 的接入说明或本次验证结果。当前架构见 ../architecture.md，当前执行证据见 ../round2-report.md。

> 历史验收记录：旧库升级/预检用例和旧程序集转发已随新项目初始化清理删除，不再是当前交付功能。当前验证以 [完成报告](embedded-approval-completion.md) 为准。

# 注册证试点实施与验证记录

日期：2026-11-01；代码基线：57f958a。用户确认采用 SQLite 与部门初审 → 质量/法规复审 → 负责人终审。

## 已实现的内容

- 真实宿主与测试共用 CertificateApplication 服务注册和 HTTP 管线；演示身份限 Development / 回环地址，非演示模式要求宿主真实认证。
- 注册证 API 统一数据权限、字段校验与分页。证号占用由数据库主键保证，提交由幂等键和内容摘要保证重放一致。
- 申请保留 BeforeSnapshot、Snapshot、来源申请和审批实例关联；登记、变更、注销使用真实业务回调，同一事务生效。
- 岗位转交/改派与处理资格受服务端控制；发起前预检并锁定已校验的 DefinitionId。版本绑定回归还发现并修正了 EF relationship fixup 导致的内存导航任务重复加入。
- 迁移基线与加固迁移、只读源库预检、升级副本、SQLite Backup API、健康/诊断端点、去重的告警日志、可重复执行的冒烟脚本和 CI 已加入。
- 前端具有稳定提交标识、字段错误、重新申请、固定历史对比、业务日期状态、分页和管理员运行检查；身份切换会清空旧敏感数据。

## 验证结果

| 检查 | 结果 |
|---|---|
| Approval.Core.Tests | Passed: 257, Failed: 0, Skipped: 0 |
| Approval.AspNetCore.Tests | Passed: 43, Failed: 0, Skipped: 0 |
| Certificate.Api.Tests | Passed: 59, Failed: 0, Skipped: 0 |
| @approval/ui | 134 项通过 |
| approval-demo | 6 项通过 |
| 总计 | 499 项通过，比基线增加 69 项 |
| Release 解决方案构建 | 0 警告、0 错误 |
| 前端类型检查、组件库与示例构建 | 通过 |
| EF migrations has-pending-model-changes | 无模型漂移 |
| 锁文件冻结安装、PowerShell 脚本语法 | 通过 |
| Release 发布包 | output/registration-pilot，包含前端与流程，未包含数据库文件 |

## 需求证据

| Requirement | Evidence |
|---|---|
| 实际三审通过后才生效 | Three_reviews_are_required_before_real_certificate_is_created |
| 未到达岗位不可提前查看/审批 | Later_reviewer_cannot_read_or_approve_before_its_stage |
| 匿名及无关用户不能读取业务数据 | Anonymous_certificate_list_is_rejected；Unrelated_user_cannot_read_request_snapshot；Lists_and_history_do_not_leak_unrelated_certificates |
| 字段、日期和规范化边界 | Invalid_registration_is_rejected_without_partial_rows；Normalization_preserves_display_value_and_accepts_exact_length_boundary；Expiry_uses_business_timezone_and_is_rechecked_before_final_effect |
| 同号并发与幂等请求只产生一份申请 | Concurrent_submissions_have_one_database_winner（不同键/相同键两组）；Reusing_key_for_different_content_is_conflict |
| 已完成请求重放不重复执行 | Retry_after_completion_replays_original_request_without_side_effects；Final_approval_retries_do_not_duplicate_business_effects |
| 变更/注销争夺同一锁时只有一个成功 | Concurrent_update_and_revoke_cannot_acquire_the_same_certificate_lock |
| 真实业务回调失败完整回滚 | Business_callback_failure_rolls_back_real_certificate_and_reservation_updates |
| 历史差异固定且变更重放不覆盖新版本 | Historical_before_and_after_survive_later_certificate_updates |
| 驳回/撤回/终止释放占用，重新申请从头审核 | Failed_registration_releases_only_its_own_number_and_reapplication_starts_over（三组） |
| 注销须全部终审人完成，证号仍被保留 | Revocation_requires_all_final_signers_and_replay_keeps_number_reserved |
| 岗位配置、转交和改派保护 | Staffing_preflight_prevents_unhandleable_applications；Transfer_addsign_and_in_place_return_cannot_bypass_pilot_roles；Administrative_reassignment_requires_current_qualification_and_preserves_deadline |
| 同一人不能承担多个不同节点 | One_person_cannot_finish_two_distinct_stages_even_after_gaining_the_role（含管理员改派拒绝） |
| 演示环境边界和生产拒绝策略 | Demo_mode_is_loopback_only_and_diagnostics_are_admin_only；Production_cannot_start_with_demo_identity_or_without_real_authentication |
| 流程版本与发布幂等 | Explicit_definition_binding_uses_the_validated_version_when_newer_version_exists；Startup_preserves_published_versions_and_explicit_publish_is_idempotent |
| 重启和租约恢复 | Host_restart_preserves_pending_stage_and_completes_the_same_application；Persisted_delivery_lease_is_reclaimed_after_restart_only_when_expired |
| 通知失败重试保持稳定幂等键 | Failed_delivery_retries_without_repeating_certificate_effect |
| 备份恢复及禁止覆盖 | Online_sqlite_backup_restores_application_links_and_pending_work；Backup_refuses_to_overwrite_source_or_existing_destination |
| 旧库安全升级及源库不变 | Legacy_upgrade_builds_a_ready_copy_without_changing_source_or_inventing_history；Legacy_preflight_rejects_unsafe_data_before_creating_destination（六组） |
| 拒绝旧库自动升级及未知迁移 | Startup_refuses_legacy_database_without_modifying_it；Startup_rejects_unknown_migration_history_without_changing_database |
| 运维状态与重试语义 | Database_busy_is_retryable_service_unavailable_not_business_conflict；Readiness_detects_missing_edit_lock_without_disclosing_database_details；Monitor_reports_changed_staffing_incident_without_repeating_unchanged_alert |
| 前端幂等、状态隔离和策略序列化 | 相同内容重试保持同一标识；新增重试发送稳定标识和来源申请；身份变化后清空旧数据，拒绝响应不会恢复前一个用户的内容；流程导入导出保留转交限制与岗位资格要求 |
| 测试项目和解决方案发现 | Certificate.Api.Tests.csproj 直接引用真实示例，已注册到 Workflow.slnx，并通过项目和解决方案入口执行 |

## 质量复核与范围

按 code-testing、test-gap-analysis、assertion-quality 复核了本次限定范围。新测试验证真实值、状态、异常、拒绝后的无副作用、快照和数据库文件，而非仅检查对象非空。并发使用两个独立 DbContext 的保存同步屏障；复制/升级测试校验源文件字节保持不变。开始阶段三个真实宿主回归在旧代码上按预期失败，随后通过。

所有业务测试使用临时 SQLite 文件、真实 CertificateService/CertificateApprovalHandler、进程内 TestServer、FakeTimeProvider 和可控 HTTP 接收替身。没有改动已有业务数据库。重启测试重建宿主/服务提供者；租约测试使用持久化的已抢占状态，不宣称已验证进程强杀、机器断电、其他数据库或实际外部通知服务。

CI 配置及对应命令已在本地校验；远端 GitHub Actions 运行需推送后完成。冒烟脚本通过 PowerShell Parser 检查，未对独立后台服务执行；后端真实管线与前端模型/类型/构建已经分别验证。真实认证接入和长期业务运行仍属于部署方后续验收。

## 复查命令

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
dotnet build 'Workflow.slnx' --no-restore --configuration 'Release'
dotnet test 'Workflow.slnx' --no-build --no-restore --configuration 'Release' --verbosity 'minimal'
dotnet ef migrations has-pending-model-changes --project 'samples/Approval.Certificate' --startup-project 'samples/Certificate.Api' --configuration 'Release' --no-build
Push-Location 'web'
pnpm install --frozen-lockfile
pnpm typecheck
pnpm test
pnpm build:lib
pnpm build
Pop-Location
```
