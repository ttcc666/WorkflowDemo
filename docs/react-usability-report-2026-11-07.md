# React 操作可靠性与效率优化

2026-11-07，本地未提交工作区，基于 HEAD `27588b1b00d36e9c91e8735094698625f30fcacc`。本轮落实六项复查建议，保留此前 React／shadcn 前端和用户未提交工作。

## 实际修改

1. `features/designer/ActionScriptEditor.tsx`：点击时捕获文档快照和 revision，同步 job 锁阻止重复调用；异步结果只用于原实例且未被再次编辑的文档。A→B→A 仍算新编辑，旧成功／失败都不能覆盖它；卸载后不回调。成功通过真实 EditorView 更新，保留受控值同步，busy 和状态提示来自现有框架组件。
2. `hooks/usePagedList.ts`、`components/ui/DataTable.tsx`、`primitives.tsx` 及所有异步列表消费者：未知总数不再回退为 0，分页区分加载／未知与确切数值，加载或总数未知时禁用页导航。表格失败提示与真正空列表分开；同身份刷新时可显示原快照，失败后恢复未知，错误和重试仍由原 ErrorState 提供。版本、审计、抄送、证书历史与交接预览同步处理。
3. `features/forms/FormDesigner.tsx`：一个字段卡片进入 Tab 顺序，方向键／Home／End 选择，Enter／空格选中，Alt+↑／↓移动，Ctrl／Meta+D 复制，Delete／Backspace 删除。按钮和快捷键共用操作，复制保持配置及唯一 key，移动／删除后焦点跟随目标，最后一个字段删除后回到字段库。子按钮键盘事件不误触卡片操作，并增加下移按钮。
4. `styles.css`：删除重复的 `.ar-pagination`、`.ar-stack`、`.ar-row` 及无用原生 select 布局规则，保留必要页面／画布样式。
5. `pages/OutboxPage.tsx`：等待查询完成后再调度下次读取；隐藏／离线时暂停，恢复时只触发一次；取消旧定时器和监听器，避免固定轮询不断中断慢查询。手动刷新和原事件投递／重试职责保持。
6. `components/ui/extended.tsx`、`features/forms/DynamicForm.tsx`、共享 `formValidation.ts`：日期原始输入即时更新，输入过程中不闪格式错误，失焦或完整提交校验时反馈；区间日历与另一端联动，完全清空的可选区间保存为 `[]`。补齐共享日期格式／真实日历日期校验，与后端现有 DateOnly `yyyy-MM-dd` 和两端区间规则一致，覆盖闰年、年份边界、无效类型、残缺区间。

除共享前端日期校验更早拒绝后端已拒绝的输入外，审批规则、Core、HTTP／JSON／SDK DTO 和数据库模型没有变化。Vue 使用同一共享校验；其其余源码未调整。UI 类型增加了 `number | undefined` 总数、表格 error、分页 loading 与日期错误展示选项，仓库消费者已同步。

## Requirement coverage

| Requirement | Evidence |
| --- | --- |
| 格式化快照早于 await，不覆盖新输入 | `formatting captures the clicked document before imports and never replaces newer input`；`script formatting cannot replace edits made while the formatting modules load` |
| 成功只应用一次，阻止重复调用与 A→B→A 旧结果 | `formatting applies one successful result and blocks duplicate requests while busy`；`formatting rejects an old result even if the user edits and returns to the same text` |
| 旧失败／卸载不破坏当前值，失败后可恢复 | `formatting ignores an old failure and cannot overwrite the current syntax error`；`formatting cannot update a destroyed editor and permits retry after a current failure` |
| pending／失败／空／有数据／恢复及分页状态正确 | `paged queries distinguish pending failure empty data refresh and recovery without a false zero total`；`a pending or failed workbench query stays unknown and an empty recovery reports zero` |
| 键盘选中、排序、边界、配置／key／焦点与子事件隔离 | `field cards use one tab stop and keyboard selection never mutates the schema`；`keyboard and button reordering preserve field values and focus and reject boundary moves`；`keyboard copy and delete preserve options and unique keys and restore an actionable focus`；`keys inside field action buttons do not invoke card deletion or duplicate handlers`；`form designer keyboard selection reorder copy and delete retain fields and focus` |
| 慢查询不被轮询取消，3 秒完成后调度，可见／联网与清理 | `outbox polling waits for a slow query to finish before scheduling the next read`；`outbox polling pauses when hidden or offline resumes once and cleans up on unmount` |
| 日期原始值即时更新，输入／提交反馈及范围正确 | `date editing updates its model immediately and shows a format error only after blur`；`dynamic date input suppresses partial feedback but submission rejects invalid raw dates`；`date range calendars enforce the opposite endpoint while text values remain editable` |
| 非法日期被拒绝且没有审批部分写入 | `invalid typed dates are rejected before a start request or any approval instance is written`；`canonical date rejects overflow before submission`；`date ranges require two canonical endpoints preserve ordering and allow optional emptiness` |

测试使用 deferred、fake timers、真实 CodeMirror 状态和实际框架浏览器操作，未依赖猜测延时或重试。输入、日期、字段结构、焦点、总数、HTTP 请求数及数据库结果使用明确断言。已有审批／并发恢复／设计器／Action／表单用例保留。

## 验证

最终修复后实际执行如下。两份完整浏览器 JSON 已解析，确认各 63 项、unexpected／skipped／flaky 均为 0。

| 命令 | 结果与证据 |
| --- | --- |
| `pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore` | 通过：225 源码边界、42 UI 消费者、3 UI 守卫／15 边界声明测试、357 单元测试（shared 17、Vue 256、React 77、Demo 6／1）、类型与各前端构建。`output/visual-review/priority-ui/final-frontend.log`。 |
| `pnpm verify:react-package` | 实际 tgz 独立安装、类型、构建、native ESM、无 Vue 依赖通过。`output/engineering/react-consumer-9d8062ca-74be-427b-9d24-1b8fb0e508d1/verification.json`。 |
| `pnpm test:e2e:react` | 完整 63/63。`output/playwright/react-1794056763351/results.json`。 |
| `pnpm test:e2e:packed` | 完整 63/63。`output/playwright/react-1794056962085/results.json`。 |
| `pnpm contracts:check` | 所有已签入基线匹配。`output/visual-review/priority-ui/final-contracts.log`。 |

首次完整浏览器的空列表夹具没有隔离其他测试留下的待办，得到 2 条而非 0。已为该用例创建独立流程并通过 `todo_definitionKey` 筛选，保留原零条断言；失败证据保留在 `output/playwright/react-1794055991760`，未以失败或旧提交结果代替最终验收。

界面证据：[新代码被保留](../output/playwright/react-1794056763351/format-preserved.png)、[失败与未知总数](../output/playwright/react-1794056763351/list-unavailable.png)、[键盘字段焦点](../output/playwright/react-1794056763351/keyboard-fields.png)。截图与临时 SQLite 由原验收脚本隔离，脚本在退出后停止本次宿主，文件留作本地证据。

尚未运行本轮 .NET 单测及 MySQL／SQL Server／PostgreSQL CI 矩阵；浏览器只使用脚本所有的隔离 SQLite。没有提交、推送、发布、启动 Docker 或修改用户开发数据库。

## 可复制命令

```powershell
Set-Location 'D:\Study\CSharp\Workflow'
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore
pnpm --dir './web' 'verify:react-package'
pnpm --dir './web' 'test:e2e:react'
pnpm --dir './web' 'test:e2e:packed'
pnpm --dir './web' 'contracts:check'
```
