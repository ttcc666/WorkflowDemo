# 第二轮嵌入式审批引擎收敛

用户附件要求本会话直接实现并验证，按阶段推进；当前分支 `codex/engineering-extensibility`，起点 `76631ca`，工作区干净。

## 范围与不变量

- 删除默认 Maintenance/Archive/Retention 的实体、服务、接口、API、UI、Worker、配置及专属测试。
- 任意宿主 DbContext 可注册；共享同一 DbContext 的业务数据、审批状态、操作记录、Outbox 在同一事务提交。
- 保留已发布版本不可变性、追加审计、并发令牌、业务唯一槽、稳定 OperationId/EventId、提交结果恢复及至少一次事件投递。
- Forms 与附件移入可选包，Core 仅保留 JSON 快照和流程变量；身份、组织、权限、消息平台、实际文件存储归宿主。
- 不新增多租户、分布式协调、维护空壳、RBAC 或自动化平台。
- 不修改历史迁移；新迁移更新模型边界，旧维护表及其数据保留为宿主管理。升级说明明确兼容边界。

## 实施清单

1. 仓库、依赖、事务保护与测试基线盘点。
2. 移除 Maintenance 完整依赖链，保留核心审计和 Outbox。
3. DbContext 泛型约束改为 DbContext；保护移入显式安装的 Interceptor；保留工作单元及命令的新作用域事务执行。
4. 去掉发布验证脚本的 ripgrep 依赖，完成 P0 编译和核心回归。
5. 拆出 Approval.Forms / Approval.Forms.AspNetCore；统一外部事件投递；移除角色推断；精简 tombstone/自动清理；附件归 Forms。
6. 两种宿主继承结构、无 Forms 与有 Forms 的真实消费者测试；四数据库、NuGet/npm 独立安装与构建。
7. 更新全部有效文档、样例、契约、包版本及升级指南；记录本次执行结果及未满足项。

## 验证与回退

每阶段编译及相关测试；最终执行后端四套测试、ReferenceHost smoke、四数据库、前端类型检查/单元测试/build/contracts、独立 NuGet/npm 消费验证。GitHub Actions 状态仅以实际远端运行为证据；当前无 Git remote 时明确记录阻碍。

迁移保持旧数据，不自动删除旧维护表/附件。回退依靠宿主备份及前版部署，不能修改已发布迁移或清除用户工作区；本轮源码以清晰的提交交付；为满足附件要求的 GitHub Actions 实际验证，已提交并推送当前开发分支。

## 执行完成

本地 504 项后端、186 项前端、7 项边界、5 项浏览器、四 NuGet/npm 独立消费者及九模型检查通过；GitHub run 41597075461 的 Windows verify 和服务三库全部 success。详细交付与升级说明见 docs/round2-report.md。
