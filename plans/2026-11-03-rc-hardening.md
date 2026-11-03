# RC 最终收尾执行记录

基线：`codex/engineering-extensibility` / `43acecca8174ad29a1c3864c2753b8dbc132a2db`，工作区干净。

## 范围与不变量

- 按用户最新说明，项目未发布，无外部消费者或生产历史数据；删除死状态不提供兼容桥。
- TimeoutScanner 的 `tenants` 直接改为 `scopes`；Scope 生命周期及数据库隔离不变。
- 全局检查确认附件两个旧维护字段没有当前业务写入者，仅旧保留测试设置 ArchiveId。
- 删除旧字段和访问分支，九套示例／测试迁移重新生成当前模型的 InitialApproval；不操作用户数据库。
- 保持真实附件授权、跨实例／草稿引用校验、写保护、OperationId 协议和 RequireOperationId 默认值。
- UI 测试注册真实 Element Plus；不关闭 warning、不降低行为断言。

## 执行清单

- [x] 修改内部命名、附件死状态及测试配置。
- [x] 用当前模型重新生成未发布迁移，替换归档保留测试为物理 schema 检查。
- [x] 更新生产 OperationId 文档、当前接入说明和公开契约记录。
- [x] 完成安全与依赖方向审查。
- [x] Windows 本机正式全量验证通过；三个服务数据库入口已执行，但镜像拉取／引擎环境失败，测试未运行。
- [x] 后续用户批准推送 fb125f0；GitHub Run 41635117047 的三个真实数据库均 31 passed／0 failed／0 skipped，Windows 全量 CI 通过，TRX／manifest／包及浏览器产物已核验。
- [x] 生成 RC 报告并保存独立提交；不 Push、不合并。最终 SHA 见正式验收副本与 git-final.json。
- [x] 根据已完成的 GitHub 补验将结论更新为 RC Ready。只推送用户批准的 RC 代码提交；补充报告为后续本地文档提交，未合并。

## 验证与回退

正式入口为 `eng/verify.ps1` 与 `eng/verify-databases.ps1`；覆盖四个后端测试项目、九套模型检查、ReferenceHost、UI/Demo、边界／契约、NuGet/npm 消费者和浏览器。

数据库结构测试查询实际系统目录，确认已删除体系的表／列未创建，保留附件元数据往返断言。
所有迁移改写仅适用于未发布基线。开发者若已应用旧迁移，应对自己的可丢弃开发库重建；不自动清库。
回退以本轮独立 Git 提交为单位，原迁移源另存于忽略的 output 证据目录。
