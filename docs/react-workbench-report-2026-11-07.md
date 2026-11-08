# React 审批工作台视觉升级验收

2026-11-07，本地未提交工作区，基于 HEAD `27588b1b00d36e9c91e8735094698625f30fcacc`。

## 设计依据

查阅了[飞书审批成员手册](https://www.feishu.cn/hc/zh-CN/articles/102254611657)、[飞书审批产品页](https://www.feishu.cn/product/approval)、[Ant Design 工作台规范](https://ant.design/docs/spec/research-workbench-cn)和[数据展示规范](https://ant.design/docs/spec/data-display-cn)。采用高频任务优先、个人审批分类、真实任务摘要、关联信息归组、明确状态和操作入口的原则。Ant Design Pro 在线预览跳转登录，未将其列表当作已观察参考。

继续使用官方 shadcn/ui Radix Nova 的 Sidebar、Card、Tabs、Field / InputGroup、Table、Avatar、Badge、Empty 和 Skeleton，不新增 UI 库或复制外部品牌、图片。

## 实际修改

- `web/apps/react-demo/src/main.tsx`、`styles.css`：深色导航、品牌入口、权限分组、浅灰工作画布、紧凑手机页头。
- `web/packages/approval-react-ui/src/pages/WorkbenchPage.tsx`：独立页头和发起入口；个人待办、超期、正在审批的发起申请、未读抄送的真实摘要；快捷筛选；任务标题、发起人、当前步骤、状态、时间与处理入口；手机卡片排列。
- `src/features/workbench/ListFilterBar.tsx`：官方 FieldGroup / InputGroup 组合，带标签的紧凑筛选，保留 URL、日期、分页、重置语义。
- `src/pages/ApprovalDetailPage.tsx`、`src/styles.css`：申请内容与申请信息／轨迹双栏，完整长编号换行，明确摘要与动作区。流程图、抄送历史、Action 记录及审批字段就绪条件保留。
- 修复抄送提交尚未结束时可提前重试的问题；恢复成功清除旧错误。工作台列表按客户端身份重新挂载，避免切换身份后继承上一人的提交状态。

摘要使用 `todo/count`、`todo?overdueOnly=true&pageSize=1`、`started?status=Running&pageSize=1`、`carbon-copies?unreadOnly=true&pageSize=1`。所有数字均为个人 API 的实际结果，失败保留未知值并提供重试。未调用管理员统计。人员名字仅在服务端授予目录权限时加载，无权限或目录不可用则显示已有 ID。

Vue、公共 API、HTTP / JSON / TypeScript 契约、Schema、数据库模型、审批规则均未调整。没有提交、推送、清理开发数据库或启动 Docker。

## 验证结果

| 验证 | 实际结果与证据 |
| --- | --- |
| `pwsh -NoProfile -File ./eng/verify.ps1 -Scope Frontend -SkipRestore` | 通过：222 个源文件边界检查，15 项边界／声明测试，320 项 Vue／shared／React 测试，以及各前端类型检查、库和 Demo 构建。 |
| `pnpm test:e2e:react` | 完整源码浏览器回归 21/21，通过；`output/playwright/react-1794022490163/results.json`。 |
| `pnpm test:e2e:react workbench.spec.ts` | 最终桌面布局补充验证 4/4，通过；`output/playwright/react-1794022715267/results.json`。 |
| `pnpm verify:react-package` | 最终 tgz 独立消费者安装、类型、构建、native ESM 和无 Vue 依赖检查通过；`output/engineering/react-consumer-dafda82b-885e-4336-9d56-698ba3e4f04a/verification.json`。 |
| `pnpm test:e2e:packed` | 完整打包浏览器回归 21/21，通过；`output/playwright/react-1794022962513/results.json`。 |
| `pnpm test:e2e:packed workbench.spec.ts` | 最终窄屏页头与身份状态整理后，打包工作台验证 4/4，通过；`output/playwright/react-1794023120558/results.json`。 |
| 人工浏览器复核 | 默认 1280px 与 390px；桌面表格不撑宽页面、处理按钮可见；手机页头保持 68px、完整字段与轨迹可读。临时视口已恢复。 |

新增测试位于 `web/tests/browser/react/workbench.spec.ts`：

| Requirement | Evidence |
| --- | --- |
| 个人真实数量、摘要快捷入口、筛选／重置 | `workbench summaries match personal API totals and shortcuts restore their complete filter scope` |
| 失败保持未知、重试、身份切换 | `workbench count failures remain unknown and recover without leaking a previous identity` |
| 提交结果丢失后按原键恢复、未读数更新 | `workbench unread recovery reuses the original operation key and updates the personal summary` |
| 桌面处理入口、手机长标题／任务上下文／详情动作 | `workbench responsive layouts keep long titles task context and detail actions within the viewport` |

首次新增夹具有两处约定错误：自审批被默认职责分离拒绝，已读接口返回 204。修正测试夹具后保留了真实后端约束。后续回归发现抄送请求未结束时可提前重试，已修复。没有放宽业务断言、删除用例或通过重试次数掩盖失败。最终结果 JSON 均为 `unexpected=0`、`skipped=0`、`flaky=0`。

本轮未重新运行 .NET 单测和服务数据库 CI 矩阵，未将构建当作后端测试通过。设计器大模块仍有既存的 Vite chunk 大小提示。

## 效果图与运行入口

截图中的申请是隔离 SQLite 测试宿主经真实 API 创建的示例数据；未写入用户正在使用的开发数据库。示例宿主在验收后停止。

![桌面工作台](../output/visual-review/workbench-desktop.jpg)

![桌面详情](../output/visual-review/approval-detail-desktop.jpg)

[手机工作台截图](../output/visual-review/workbench-mobile.jpg) · [手机详情截图](../output/visual-review/approval-detail-mobile.jpg)

本地开发前端继续运行：<http://localhost:5174/workbench>；共享后端：<http://localhost:5199>。
