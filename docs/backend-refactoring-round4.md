# 后端重构第四轮：Authorization 与 DI

2026-11-03，本轮完成内部职责与构造依赖重构。公共授权 / 注册入口和业务规则保持，数据库专项通过 GitHub CLI / CI，未在本地执行或打开 Docker。

- 新增一个 internal ApprovalResourceReader，集中 Task / Instance / Definition / Message / Outbox 资源规范化及 QueryScope 调用，保留原 Extension 写权隔离、超时、取消和错误映射。
- DefaultApprovalAuthorization 保留可信 Actor、防伪 userId、权限码、系统权限白名单与 Provider 判断；不缓存 Allow，不把资源解析当成授权。
- AddApprovalWorkflow<TContext> 调用五个 private registration 方法：Persistence、Authorization、Runtime、Query、Background。没有新增公共注册 API、接口、程序集或依赖。
- Engine、Definition、Outbox 的可选依赖从属性赋值迁入 internal 构造函数；公共构造方式保留，internal 属性只读，使用既有 HostServices 安全包装。
- 保持 Singleton / Scoped / TryAdd / Replace 的实际规则，未夹带重复注册拒绝行为或删除重试 scope factory。

每个主题独立 Release solution build 最后 0 warnings / 0 errors。资源 / 权限 / 回滚相关 70 passed；构造注入 24 passed；Composition / lifecycle / background / host context 相关 15 passed，均0失败跳过。

| Requirement | Evidence |
| --- | --- |
| 身份防伪、授权失败关闭、系统权限限制 | `PermissionTests.ActorSpoofAndMissingIdentity_AreRejectedBeforeProvider`、`ProviderReceivesCanonicalResource_RevocationHasNoCachedAllow`、`RestrictedSystemActor_CannotPublishOrImpersonateUser` |
| 同一 scope 共用 Host DbContext，跨 scope 隔离 | `CompositionRootTests.CompositionRoot_ValidatesScopesAndSharesOnlyTheHostContextWithinEachScope` |
| Root 不能取得 Scoped Engine，Signal 为 Singleton | 同一用例的具体 Throws / Same / NotSame 断言，ValidateOnBuild / ValidateScopes 均启用 |
| 后台与可选模块注册不变化 | BackgroundRegistration / HostDbContextIntegration / FormsBoundary 原用例通过 |

本轮仅内部 C# 结构变化，数据库模型、HTTP JSON、公共 nullable 契约不变。未来重复注册策略若要从 last-wins 改为拒绝，应作为明确配置行为变化处理，未静默改动。

第三轮提交 `896befad5573a5a693f3562c463af48557dcbc5a` 的 GitHub Run `41661918415` 已完整 success。三种服务数据库各 31 passed / 0 failed / 0 skipped，manifest.head 与该提交一致，证据位于 `output/backend-refactoring/ci/41661918415`。此证据属于前三轮；本轮及最终代码仍需在新提交的 CI 补验。

补验已完成：本轮提交 `d7a1404e4eb144361d0e8016352d987e2277a760` 的 CI41666657881 成功；全部五轮最终代码 `f6342b91569ed3ac66f01a42ea7ac09903e55108` 的 [CI41674494343](https://github.com/ttcc666/WorkflowDemo/actions/runs/41674494343) 也全部成功，三服务 Provider各33passed / 0failed / 0skipped及Windows完整门禁通过，详见最终报告。
