# React 审批工作台视觉升级

## 范围与不变量

用户已要求搜集资料并实施美化。升级 React Demo 导航、工作台、筛选布局、审批详情和轨迹；继续使用已安装的 shadcn/ui / Radix Nova。Vue、后端、HTTP / JSON / Schema、数据库模型与审批业务规则不变。

摘要仅使用当前用户的 todo/count、todo(overdueOnly)、started(status=Running)、carbon-copies(unreadOnly) 的真实结果。加载和失败不能伪装为 0；普通用户不调用管理员统计。筛选、分页、抄送已读的稳定操作键、详情字段权限、审批动作就绪条件与未知提交恢复保留。

## 资料与取舍

- [飞书审批成员手册](https://www.feishu.cn/hc/zh-CN/articles/102254611657)：以待办、已办、发起、抄送组织个人审批；发起入口与管理入口区分。
- [飞书审批产品页](https://www.feishu.cn/product/approval)：观察桌面、移动界面采用统一入口与清晰内容分区。只借鉴信息结构，不复制品牌或引入未实现业务。
- [Ant Design 工作台规范](https://ant.design/docs/spec/research-workbench-cn)：首屏优先展示高频任务，缩短处理路径，按权限展示内容。
- [Ant Design 数据展示规范](https://ant.design/docs/spec/data-display-cn)：按重要程度、操作频率和关联编排；完整展示时间、状态、操作；考虑空数据、长文本。
- [shadcn Card](https://ui.shadcn.com/docs/components/radix/card)、[Tabs](https://ui.shadcn.com/docs/components/radix/tabs)、[Sidebar](https://ui.shadcn.com/docs/components/radix/sidebar)：沿用官方组件组合与语义主题。

Ant Design Pro 在线预览进入登录页，未将其列表当成已观察参考。采用其公开设计规范，不安装 Ant Design。

## 实施清单

- [x] 更新语义色板、导航、页头与页面间距。
- [x] 工作台真实摘要、线形分类导航、紧凑筛选、任务信息层级与明确详情入口。
- [x] 详情主内容、右侧轨迹、清晰摘要与操作区；保留流程图、抄送、Action 记录。
- [x] 适配手机，保留所有业务字段，支持长标题与多行筛选。
- [x] 验证真实摘要、筛选 URL、失败反馈、身份切换、抄送已读，以及既有审批／设计器回归。
- [x] 前端门禁、源码／包消费者浏览器验证、桌面／手机截图。

已完成。完整证据见 `docs/react-workbench-report-2026-11-07.md`。

## 回退

仅回退本次涉及的 React 视图与 CSS 差异。保留此前已完成的 React 功能迁移和 shadcn 组件；不重置工作区，不删除开发数据。
