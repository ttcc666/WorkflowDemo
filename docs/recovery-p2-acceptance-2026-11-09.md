# Vue / React 恢复与站内信修复验收

本轮基于 `codex/react-production-audit` 的 `5be71467e0b8644c342b6594982570ff8b90b567`，完成恢复组件与站内信的修复和验证。修改在该分支提交，不直接修改 main。远程验收必须对应修复提交的确切 SHA，历史提交的 CI 不代表本轮修改已获远程验收。

## 根因与行为

- 恢复组件默认只查看内存中的单个 `pending`，刷新后无法呈现存储里已有的全部记录。现在通过现有 shared StateCell 发布记录变更，React/Vue 默认列出所有 scope；显式 scope/null 保持选择/隐藏语义。
- 每行保留独立重试、确认和错误状态。人工确认只删除对应 scope 及已有别名；其余记录不受影响。刷新后没有原始正文时禁止重试；同页重试继续使用原始正文和原始幂等键。
- React 列表按身份重新挂载，避免旧身份未完成恢复污染新身份的 busy 状态。两端验证多行并行恢复互不影响。
- React MessageBell 原来只防身份切换，没有防用户主动导航。现在比较导航版本，覆盖不同页面、同路径查询、A→B→A、目标 loader 仍等待，以及普通声明式 Router。路由变化不会取消已读请求，完成后仍刷新计数，但不能覆盖用户选择。
- 恢复后的导入使用原 scope；设计器/发起页仅在 scope 对应当前对象时刷新或处理结果，避免多 scope 恢复误作用于当前编辑对象。

## 关键文件

- `web/packages/approval-shared/src/commands/commandIntent.ts`：记录变更通知、存储禁用时的枚举、按 scope 清理 pending。
- `web/packages/approval-react-ui/src/components/CommandRecovery.tsx`、`src/hooks/useCommandIntent.ts`：全量发现、逐行恢复、身份隔离和响应式订阅。
- `web/packages/approval-react-ui/src/components/MessageBell.tsx`：导航版本、进行中 Data Router 导航和旧生命周期保护。
- `web/packages/approval-ui/src/components/CommandRecovery.vue`、`CommandRecoveryItem.vue`、`src/composables/useCommandIntent.ts`：Vue 对等实现。
- 两端 Designer/StartForm 与 React ImportDefinitionDialog：恢复结果绑定原对象。
- shared `recoveryScopes.test.ts`、两端 `CommandRecovery.test.*`、React `MessageBell.test.tsx`、浏览器 `recovery-p2.spec.ts`：回归证据。

## 行为证据

| Requirement | Evidence |
|---|---|
| 刷新发现多笔、无正文不重试、只清理一笔 | `recreates all scopes without bodies or new keys and resets only the reviewed scope`；React/Vue `refresh discovers all opaque recovery records and review clears only one without a write` |
| 同页保留原正文/原键，隔离其他记录 | `retries only the live original body and key and leaves another refreshed scope untouched`；`Vue retries the live original body and key and keeps other records visible` |
| 同源更新、显式 scope/null、存储不可用与别名隔离 | `reacts to later peer writes and clears and preserves explicit scope and explicit null`；`Vue reacts to peer changes and preserves explicit scope null and identity isolation`；`keeps independent in-memory retries and keys when storage is unavailable`；`keeps users and legacy namespace and scope aliases isolated during discovery` |
| 多笔并行、旧身份 busy 隔离 | `keeps concurrent recovery rows independent until both original operations settle`；`Vue keeps concurrent recovery rows independent until both original operations settle`；`isolates the new identity recovery status while an old identity retry is still pending` |
| 正常跳转、失败后原键重试、旧身份无副作用 | `a deferred unread message navigates normally when no navigation happened`；`a failed deferred read stays on the page and a new click reuses the same key before normal navigation`；`a deferred old identity message cannot navigate or update the new identity count` |
| 用户主动导航与第三方宿主 | `a deferred unread message preserves the user navigation %s without cancelling its write`（other/same-path/back）；`preserves a user navigation whose destination loader has not completed yet`；`preserves navigation in a declarative host router while the original read completes`；`a real committed unread-message request cannot overwrite navigation chosen in the global layout` |

竞争场景通过显式 deferred Promise 同步。新增 React 身份忙碌隔离与未完成 loader 导航测试均先复现失败，再验证修复通过。浏览器导航测试改为等待目标 URL/heading，再放行响应并检查服务器的准确未读计数及目标消息最终已读状态；全套中还修正了“当前用户只有这一条未读消息”的测试数据假设。没有修改重试次数或超时来掩盖失败。

## 验证

本轮本地验证结果如下，均来自上述基准上的最终修复工作区；未用历史 CI 替代本轮结果。

| 检查 | 实际结果 |
|---|---|
| Release 构建 | 通过，0 警告 / 0 错误 |
| 后端测试 | 988 通过，0 失败 / 0 跳过：Core 694、HTTP 148、默认数据库组 87、Certificate 59 |
| 前端 Vitest | 498 通过：shared 45、React UI 176、Vue UI 266、React Demo 5、Vue Demo 6 |
| 边界与 React UI 审计测试 | 18 通过；lint、架构边界和 UI 审计通过 |
| TypeScript / vue-tsc、四项库与 Demo 构建 | 全部通过 |
| contracts:check、Model Drift | 契约匹配全部基线；九个 Context 无模型变化 |
| ReferenceHost、NuGet 独立消费者 | ReferenceHost 27 项 smoke；Core-only 19 项、TestingHost 22 项检查通过 |
| Vue 真实 tgz 浏览器 | 12 通过，0 skipped / unexpected / flaky，已解析 JSON 报告 |
| React 真实 tgz 独立消费 | typecheck、build、native ESM 通过；无 Vue 依赖 |
| React 源码浏览器 | 76 通过，0 skipped / unexpected / flaky，退出 0，已解析 JSON 报告 |
| React 打包浏览器 | 77 通过，0 skipped / unexpected / flaky，退出 0，含第三方独立宿主真实审批 |
| 单次完整 verify.ps1 | 退出 1：默认 npmmirror 持续 HTTP 500，在 React 消费者安装阶段主动中止；后续全部包门禁使用进程级官方源补跑成功，未宣称该完整命令退出 0 |

实际执行的命令（根目录，日志见下表）：

```powershell
$env:APPROVAL_BROWSER_CHANNEL = 'chromium'
pwsh -NoProfile -File './eng/verify.ps1'

# 默认镜像阻塞后，进程级覆盖；未修改仓库/用户 npm 配置。
$env:npm_config_registry = 'https://registry.npmjs.org/'
pwsh -NoProfile -File './eng/verify-packages.ps1' -SkipBuild -RunBrowser
pnpm --dir 'web' verify:react-package '--skip-build'
pnpm --dir 'web' test:e2e:packed
pnpm --dir 'web' test:e2e:react
pnpm --dir 'web' lint
```

| 证据 | 路径 |
|---|---|
| 完整命令的已通过前序门禁与镜像错误 | `output/p2/verify-all-final.log` |
| 官方源 NuGet/Vue 包与 12 项浏览器 | `output/p2/packages-final.log`；`output/engineering/consumer-cb80004d18c04d5ba8a6a5dfb3b447d7/browser/results.json` |
| 官方源 React 独立包 | `output/p2/react-package-final.log`；`output/engineering/react-consumer-f283dbeb-dcf0-4b86-8cfb-355a1a19eca6/verification.json` |
| 最终源码 76 项 | `output/p2/browser-source-accepted.log`；`output/playwright/react-1794236862102/results.json` |
| 最终打包 77 项 | `output/p2/browser-packed-final.log`；`output/playwright/react-1794237149417/results.json` |
| 最终 lint 与机器汇总 | `output/p2/lint-final.log`；`output/p2/acceptance.json` |

早期失败、两条先失败后修复的单元回归，以及定向浏览器结果均保留在 `output/p2/`。没有删除或跳过失败场景。

完整复核，在本 worktree 根目录运行：

```powershell
$env:APPROVAL_BROWSER_CHANNEL = 'chromium'
$env:npm_config_registry = 'https://registry.npmjs.org/'
pwsh -NoProfile -File './eng/verify.ps1'
```

源码浏览器复核，在 `web/` 下运行：

```powershell
$env:APPROVAL_BROWSER_CHANNEL = 'chromium'
pnpm test:e2e:react
```

## 契约与限制

无后端 API、HTTP/JSON/生成 TypeScript 契约或数据库模型变化；无新增依赖。前端 Hook/manager 增加供订阅使用的 revision，恢复组件 reset 回调附带 scope；原无参数回调仍可使用。请求正文仍仅在内存中；持久化格式、fingerprint、键生成与权限机制保留。

Data Router 的同步导航观察使用 React Router 7 导出的 `UNSAFE_DataRouterContext`；普通 Router 有独立路径和回归测试。React Router 主版本升级时应复核该集成点。本地使用 Chromium；服务数据库矩阵按仓库要求仅在 GitHub Actions 执行。修复提交对应的 CI Run URL、确切 SHA 和各 Job 结果在交付记录中提供。

本轮可进入代码审查。是否合并 main：待用户审查、提交后取得该 SHA 的 Windows 与三服务数据库 CI 验收结果，不作无条件合并结论。
