# shadcn/ui 基础组件迁移

用户已选择 shadcn/ui（保留 Radix），本次按当前会话的前端实施授权执行。

- 范围：React UI 基础控件、表单布局、反馈 / 弹层、数据表、日历与 Demo 导航；官方组件源码统一放在 `src/components/shadcn/`。
- 不变量：React 19 / Tailwind v4 / Radix 保留；Vue、后端、HTTP / JSON / Schema、权限与命令恢复语义不变。已有 React 公开组件 props 由薄适配层保留。
- 步骤：CLI 官方组件 → 审核源码 / 相对导入 / 前缀主题 → 替换适配层 → 删除重复控件样式 → 单元与浏览器 / 实际 tgz 验证。
- 验证：焦点 / Escape、disabled / busy、选择值 / 清空、单选组隔离、日期边界、键盘搜索、字段错误、CSS / 声明 / 无 Vue 依赖；原审批 / 双向设计器 / Action / 表单 E2E 继续执行。
- 回滚：仅恢复本次 React UI 适配、官方组件与配置；不删除或重置数据库，不影响 Vue；不提交 / 推送。

状态：实施及本地验收完成。完整 Frontend 门禁、52 个 React UI 测试、源码 / 实际 tgz 浏览器各 17 场景通过。证据见 [迁移验收](../docs/react-shadcn-report-2026-11-07.md)。未提交 / 推送 / 发布。

## 逐页组件复查补充

2026-11-07 用户指出原生控件／手写 UI 仍有残留，继续本计划的已授权实施。将 NativeSelect、浏览器日期、10 处 details、2 处 datalist、手写统计块、轨迹／附件／步骤改为官方 Select、Calendar／Popover、Accordion、Command、Card、Item，清理旧外观覆盖。

不变量继续保持；通用 React Select props 有意统一为 onValueChange(value)，没有增加未发布旧 API 的兼容层。验收包括每页 JSX 来源检查、真正的 Radix 交互、输入保留、禁用时关闭弹层、日历边界、完整业务和 tgz 回归。最终逐页证据见 [框架 UI 验收](../docs/react-framework-ui-report-2026-11-07.md)。

补充阶段已完成：Frontend 330 项单元测试、源码／打包浏览器各 59 项、UI 来源守卫、契约和独立 tgz 消费者通过。连线验收发现的点击节点遮挡已修复，保留精确拖放及原连线／JSON 断言。临时浏览器视口与隔离验收宿主已清理，未提交／推送。
