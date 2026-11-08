# shadcn/ui 迁移验收

用户选择：保留 React 19 / Tailwind v4 / Radix，使用官方 shadcn/ui。

## 实现

- CLI 4.23.0 生成官方 Radix Nova 的 32 个组件文件及 use-mobile hook，来源记录于 components.json 和 THIRD_PARTY_NOTICES.md。
- primitives / extended / DataTable 通过官方 Button、Input、Native Select、Field、Card、Table、Dialog / AlertDialog、Sheet、Popover、RadioGroup、Calendar、Command、Sonner 等实现，现有公开 props 保留为薄适配接口。
- Demo 使用官方 Sidebar、Breadcrumb、Avatar、Separator；人员目录继续调用原 API，使用 Command + Popover。
- 删除重复的通用控件样式；主题使用语义 token、approval 前缀与官方状态 / 动画样式。基础重置限定审批容器，业务画布和布局继续保留。
- 公开 JS / d.ts 使用包内相对导入，声明依赖由宿主构建；宿主不需要安装 shadcn CLI。MIT 来源说明随包分发。

Vue、后端业务、HTTP / JSON / Schema、数据库模型和命令意图规则未在本迁移中修改。React 表单 radio 控件改用官方 RadioGroup；人员搜索保留原值 / 排除 / 状态 / 去重，具有 combobox 语义；确认窗口具有 alertdialog 语义。

## 实际验证

在仓库根目录：`pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore`，退出 0。

| 检查 | 结果 |
| --- | --- |
| UI 单元 / 组件 | React UI 52、shared 5、React Demo 1；Vue UI 256、Vue Demo 6，共 320 个通过 |
| 边界 / 声明工具 | 222 源文件检查；15 个工具测试通过 |
| 类型 / 构建 | 所有源码与测试类型检查、Vue / React 库及两套 Demo 构建通过 |
| 最终源码 E2E | 17/17，0 skipped / unexpected / flaky；output/playwright/react-1794020567399/results.json |
| 最终 packed E2E | 17/17，0 skipped / unexpected / flaky；output/playwright/react-1794020434406/results.json |
| 真实 tgz | output/engineering/react-consumer-f8f97ced-8195-4f8f-ade2-f0ac43b6b4cf/verification.json；安装 / 类型 / 构建 / native ESM / CSS / 无 Vue 依赖检查通过 |
| 视觉 | 默认视口与 390px；官方手机 Sheet 可用 Escape 关闭，无整页水平溢出；预览 output/visual-review/shadcn-expense-*.jpg |

## 关键证据

| Requirement | Evidence |
| --- | --- |
| 字段错误关联且保留值 | `exposes field validation and descriptions to controls without changing their values` |
| 失败保留确认窗口 / busy / 焦点恢复 | `keeps a controlled confirmation open after failure and restores trigger focus when canceled` |
| Native Select 的清空 / disabled / size | `keeps native select clear and disabled behavior through the official control` |
| 单选组相互隔离 | `Radio groups in separate form instances do not change one another`：独立值和回调断言，替代旧 DOM name 断言 |
| 日期 / 搜索键盘 | `Calendar navigates months and keeps date boundaries disabled`；`Command supports keyboard selection and returns the selected value` |
| Schema / 设计器完整性 | `React and Vue preserve all field node and Action configuration on both editing directions`；真实连线 / 拖入 / undo / redo 场景 |
| 审批原键恢复 / Action / 交接 / 抄送 | 原有 17 个端到端场景及其最终状态、请求正文 / 原键和审计断言保留 |

审查修复：生成文件的内部 alias、CommandDialog Title 位置、空前缀类、Native Select 适配、busy Spinner 可访问名称、cmdk 标签与列表 label、Portal 主题作用域、官方状态 variants、jsdom 缺失 scrollIntoView 和 Demo 图标直接依赖。浏览器测试仅按新控件角色 / Portal 结构更新定位；业务断言未放宽。

本报告为当前 HEAD 上未提交源码的本地证据。未运行 mutation tool 或覆盖率测量；没有本次 SHA 对应 CI。未提交、推送、合并或发布。
