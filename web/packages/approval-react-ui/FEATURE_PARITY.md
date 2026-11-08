# Vue / React 功能对照与验收基线

日期：2026-11-06。Vue 参考 HEAD：`27588b1b00d36e9c91e8735094698625f30fcacc`。

**React 组件库、共享核心和 Demo 已实现，本地完整门禁及源码 / 打包浏览器验收通过。** 实施顺序见 [`plans/2026-11-06-react-frontend-parity.md`](../../../../plans/2026-11-06-react-frontend-parity.md)，实际结果见 [验收报告](../../../../docs/react-frontend-report-2026-11-06.md)。

## 使用规则

- 下表编号保留原验收范围；实际实现与精确用例映射见文末 K01–K10。已实现表示源码对照完成，自动化证据按组合场景及共享回归记录，不表示每个细节都有独立浏览器用例。
- 每个编号是一组明确验收点；行内所有场景都通过才可完成，不能只凭页面存在、按钮出现、编译成功或 mock 返回成功打勾。
- 最终报告记录命令、输出、结果、参考 HEAD 和未提交工作区。未推送的代码没有新的 CI SHA；不得把本地结果写成 CI 通过。
- Vue 参考路径 `V/` = `web/packages/approval-ui/src/`；`D/` = `web/apps/demo/src/`；未列全的子组件继续从对应 feature 追踪，不视作排除项。
- `R/` = `web/packages/approval-react-ui/src/`；`RD/` = `web/apps/react-demo/src/`。
- 原测试组 T01–T10 依次对应 transport/client、intent、lists/messages、approval、forms、flow/designer、governance/operations、真实业务 E2E、双向兼容/Action E2E、独立包。最终实际证据索引为 K01–K10，其中列出精确用例。
- 原需求的正则、通用字段联动、HTTP 响应映射、任意 Mapping 表达式与当前协议有差异，见末尾差异表；不能创建仅 React 能识别的配置。

## 页面概览

| Vue 页面 / 组件 | React 对应实现（实际） | 关联验收编号 | 测试 | 状态 |
| --- | --- | --- | --- | --- |
| V/views/WorkbenchView.vue | R/pages/WorkbenchPage.tsx | W01–W06、E01–E10 | T03、T08 | 已实现；见关联编号 |
| V/views/ApprovalDetailView.vue | R/pages/ApprovalDetailPage.tsx | D01–D18、P01–P12、E01–E10 | T02、T04、T08 | 已实现；见关联编号 |
| V/views/StartCenterView.vue | R/pages/StartCenterPage.tsx | S01–S04 | T03、T08 | 已实现；见关联编号 |
| V/views/StartFormView.vue | R/pages/StartFormPage.tsx | S05–S08、FV01–FV16、FM01–FM10 | T02、T05、T08 | 已实现；见关联编号 |
| V/views/DefinitionsView.vue | R/pages/DefinitionsPage.tsx | G01–G12 | T07、T09 | 已实现；见关联编号 |
| V/views/DesignerView.vue | R/pages/DesignerPage.tsx | F01–F19、N01–N08、A01–A15、FD01–FD09 | T06、T09 | 已实现；见关联编号 |
| V/features/forms/FormDesigner.vue | R/features/forms/FormDesigner.tsx | FD01–FD09、FV01–FV16、FM01–FM10 | T05、T09 | 已实现；见关联编号 |
| V/views/MonitorView.vue | R/pages/MonitorPage.tsx | O01–O04、P08 | T07、T08 | 已实现；见关联编号 |
| V/views/OutboxView.vue | R/pages/OutboxPage.tsx | O05–O08、P11 | T02、T07、T09 | 已实现；见关联编号 |
| V/views/DiagnosticsView.vue | R/pages/DiagnosticsPage.tsx | O09–O10、P12 | T07、T08 | 已实现；见关联编号 |
| V/components/MessageBell.vue | R/components/MessageBell.tsx | O11–O13 | T02、T03、T08 | 已实现；见关联编号 |
| V/features/flow/FlowViewer.vue | R/features/flow/FlowViewer.tsx | F17、N01–N08 | T06、T09 | 已实现；见关联编号 |
| D/证书与 Webhook | RD/证书与 Webhook | C01–C08 | T08、T09 | 已实现；见关联编号 |

## 工作台

参考 `V/features/workbench/{TodoTable,InstanceTable,CarbonCopyTable,ListFilterBar}.vue`、`V/composables/usePagedList.ts`。

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| W01 | 我的待办：名称、标题、发起人、节点、时间、超期、数量、搜索 / 筛选 / 分页与详情导航 | T03、T08 | 已实现；K01 |
| W02 | 我已处理：状态、结果、创建 / 处理时间、流程 / 状态 / 时间过滤与分页 | T03、T08 | 已实现；K01 |
| W03 | 我发起的：状态、当前处理人 / 节点、标题、时间 / 关键词与分页 | T03、T08 | 已实现；K01 |
| W04 | 抄送我的：未读、时间、来源、内容、只看未读、点击按个人标记已读并进入详情 | T02、T03、T08 | 已实现；K01 |
| W05 | 各标签独立 URL 筛选 / 页码 / 页长，刷新和浏览器前进后退恢复，搜索后页码规则与 Vue 一致 | T03 | 已实现；K01 |
| W06 | 切用户重载数量与列表；显式刷新；旧查询 / 迟到错误不能覆盖新结果；加载 / 错误 / 空态完整 | T01、T03、T08 | 已实现；K01 |

## 审批详情、动作与异常恢复

参考 `V/features/approval/`、`V/views/ApprovalDetailView.vue`、`V/components/CommandRecovery.vue`、`V/composables/useCommandIntent.ts`。

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| D01 | Summary：名称、标题、实例 ID、发起人、状态、节点、起止时间、业务类型 / 编号；绑定发布版本 | T04、T08 | 已实现；K02 |
| D02 | 同意：任务 / 节点能力、意见要求、仅提交可编辑字段、变更确认、成功刷新 | T02、T04、T08 | 已实现；K02 |
| D03 | 驳回：必填原因、确认、结束流程；不能混用退回能力 | T04、T08 | 已实现；K02 |
| D04 | 退回：真实 returnTargets、选择指定节点 / 发起人、加载 / 错误、原因与权限 | T04、T08 | 已实现；K02 |
| D05 | 转交：搜索单人、排除无效对象、确认、后端资格限制与最新任务 | T04、T08 | 已实现；K02 |
| D06 | 加签：多人选择、Before / After / Parallel、意见规则、重复提交防护与实际审批顺序 | T04、T08 | 已实现；K02 |
| D07 | 撤回：使用实例 canWithdraw、确认和意见，刷新实例状态 | T04、T08 | 已实现；K02 |
| D08 | 重新提交：Resubmit 任务、从服务端当前数据恢复表单、修改 / 校验 / 提交、轮次轨迹 | T04、T05、T08 | 已实现；K02 |
| D09 | 管理终止：Running / Returned 状态、实例能力、原因确认 | T04、T08 | 已实现；K02 |
| D10 | 管理改派：待办资源能力、人员选择、原因确认；不能凭 isAdmin 放行 | T04、T08 | 已实现；K02 |
| D11 | 表单只读 / 可编辑 / 隐藏、附件授权下载、仅可编辑字段的变更说明 | T04、T05、T08 | 已实现；K02 |
| D12 | 字段级 400 错误定位（含明细行路径）、保留用户输入；其他结果刷新最新状态 | T02、T04、T05 | 已实现；K02 |
| D13 | Timeline：审批人、意见、时间、返回 / 加签 / 转交 / 多轮次、字段差异与服务器投影 | T04、T08 | 已实现；K02 |
| D14 | CarbonCopyHistory / RecipientsDialog：抄送记录、逐人已读、分页、错误 / 空态 | T04、T08 | 已实现；K02 |
| D15 | ActionExecutionList：步骤 / 模式 / 状态 / 时间 / 错误 / 输出的授权视图、Outbox 导航 | T04、T09 | 已实现；K02 |
| D16 | 稳定键：scope / 身份、正文快照、对象排序摘要 / 数组顺序、并发运行保护、同键不同正文拒绝 | T02、T08 | 已实现；K02 |
| D17 | 恢复：网络 / 5xx / CommitOutcomeUnknown 保留原键和原正文；uncertain 后 4xx 不自动清除；页面内原内容重试 / 结果确认 | T02、T08 | 已实现；K02 |
| D18 | sessionStorage 仅键 / 摘要 / 状态，Storage 不可用 / 删除失败 fallback；页面丢失原正文不假称可恢复；新意图确认；切身份不呈现旧结果 | T01、T02、T08 | 已实现；K02 |

## 发起中心与申请草稿

参考 `V/views/StartCenterView.vue`、`StartFormView.vue`。

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| S01 | 真实 startableForms、分类、关键词、名称 / 描述与进入发起表单 | T03、T08 | 已实现；K02 |
| S02 | Forms 未启用 / 无发起能力时的菜单、路由和提示 | T01、T08 | 已实现；K02 |
| S03 | 我的草稿、最后保存时间、继续编辑正确 key / draftId、已失效流程提示 | T03、T08 | 已实现；K02 |
| S04 | 删除草稿确认、稳定键、异常恢复、刷新；不能读 / 删他人草稿 | T02、T08 | 已实现；K02 |
| S05 | 加载真实 startForm、表单默认值、人员、文件及意见，不硬编码表单 | T05、T08 | 已实现；K02 |
| S06 | 保存未完成草稿 / 恢复、formula 和明细数据完整、原键恢复 | T02、T05、T08 | 已实现；K02 |
| S07 | 提交、服务端字段错误、业务标题模板、成功后草稿处理与详情导航 | T02、T05、T08 | 已实现；K02 |
| S08 | 路由 / 身份变化时取消或忽略旧 form / draft 响应；无权限 / 无数据 / 网络状态 | T01、T03、T08 | 已实现；K02 |

## 全部表单字段

每行均须验证设计 / 序列化 / 默认值 / 输入 / 只读 / 编辑权限 / 服务端校验兼容。参考 `V/contracts/forms.ts`、`V/features/forms/{formFields,formSerialization,formValidation,formFormulas,formula}.ts`、FieldInput / ValueText。全部测试 T05、T08、T09。

| ID | 类型 | 额外验收点 | 状态 |
| --- | --- | --- | --- |
| FV01 | Text | 长度、trim 校验、占位符 / 提示 | 已实现；K03 |
| FV02 | TextArea | 多行、长度、说明 | 已实现；K03 |
| FV03 | Number | min / max、空值、小数位、非有限值 | 已实现；K03 |
| FV04 | Money | 金额精度 / 展示及边界 | 已实现；K03 |
| FV05 | Formula | 自动只读、变量、依赖排序、循环 / 无效表达式、null 与舍入 | 已实现；K03 |
| FV06 | Date | 原日期字符串格式、清空 / 默认值、时区不改日期 | 已实现；K03 |
| FV07 | DateRange | 数组格式、开始结束顺序、空值 | 已实现；K03 |
| FV08 | Select | label/value、清空、默认选项与后端允许值 | 已实现；K03 |
| FV09 | MultiSelect | 数组、选择 / 删除、顺序与无效选项拒绝 | 已实现；K03 |
| FV10 | Radio | 单值、选项配置与默认值 | 已实现；K03 |
| FV11 | Switch | Boolean、必填必须 true，不能转成字符串 | 已实现；K03 |
| FV12 | User | 单人值格式、搜索 / resolve、失效人员与后端规范化 | 已实现；K03 |
| FV13 | Users | 多人格式、去重 / 上限、身份隔离、后端规范化 | 已实现；K03 |
| FV14 | Table | 行增删 / 上限、6 类合法列、行公式 / 列合计及校验路径 | 已实现；K03 |
| FV15 | File | 元数据数组、上传 / 类型 / 数量 / 大小限制、鉴权下载、错误 / 上传中 | 已实现；K03 |
| FV16 | Description | 帮助内容、无业务数据 / 必填校验、不注入 HTML | 已实现；K03 |

## 表单共同规则与设计器

| ID | Vue 来源 / 验收点 | React 对应（实际） | 测试 | 状态 |
| --- | --- | --- | --- | --- |
| FM01 | formFields：全部默认值 / initialValues、已有草稿恢复 | shared forms + DynamicForm | T05 | 已实现；K03 |
| FM02 | formValidation：必填、maxLength、min/max、precision、行数 / 人数 / 文件数 | shared validation + FormField | T05、T08 | 已实现；K03 |
| FM03 | formula：全部原函数 / 语法 / 限长 / 精度 / 大小写、同一 formula-cases fixture | shared formula + FormulaInput | T05 | 已实现；K03 |
| FM04 | formFormulas：行内 Formula→sum→顶层依赖，不自行发明联动协议 | shared formulas + DynamicForm | T05、T08 | 已实现；K03 |
| FM05 | DynamicForm：编辑 / 只读及 editableFields 大小写规则 | R/features/forms/DynamicForm.tsx | T05、T08 | 已实现；K03 |
| FM06 | withoutFields / pickValues：隐藏投影和提交仅允许字段；审计 / 附件同样受授权 | shared serialization + 详情 | T05、T08 | 已实现；K03 |
| FM07 | FormTableField：Text、Number、Money、Date、Select、Formula 列，无 Table/File 等非法嵌套 | R/features/forms/DynamicForm.tsx | T05、T09 | 已实现；K03 |
| FM08 | FileUploadInput/FileList：文件限制、存储错误、元数据、授权 Blob、revoke URL；上传不自动重试 | R/features/forms/FileInput.tsx | T01、T05、T08 | 已实现；K03 |
| FM09 | UserPicker/useDirectory：搜索、多选、已选删除、resolve 缓存、角色、目录 501 与加载 / 空态 | R/components/UserPicker.tsx | T01、T05 | 已实现；K03 |
| FM10 | cleanField/cleanSchema/cleanSettings：可选值、省略规则、跨度、类型变更，不改变协议 | shared forms | T05、T09 | 已实现；K03 |
| FD01 | FormDesigner 字段面板分组、16 类型点击 / 拖入 | FieldPalette | T05、T09 | 已实现；K03 |
| FD02 | 画布排序、选中、拖动位置、复制新 key、删除与选择恢复 | FieldCanvas | T05、T09 | 已实现；K03 |
| FD03 | span、两列 / 整行、Table / Description 布局及预览 | FieldCanvas + DynamicForm | T05、T09 | 已实现；K03 |
| FD04 | FieldInspector 名称 / key / 类型、默认值、必填、占位符 / help、合法 key / 重复 key | FieldInspector | T05、T09 | 已实现；K03 |
| FD05 | 数字边界 / 精度、文字长度、人员和文件属性 | FieldInspector | T05 | 已实现；K03 |
| FD06 | OptionsEditor 选项 label/value、增删 / 排序、默认选择 | OptionsEditor | T05 | 已实现；K03 |
| FD07 | FormulaInput 数值 / sum 变量、不含自身、实时解析错误 / 预览 | FormulaInput | T05、T09 | 已实现；K03 |
| FD08 | 明细列增删 / 类型 / key / options / required / formula / sum | TableColumns | T05、T09 | 已实现；K03 |
| FD09 | 预览→编辑、Schema 改动进入流程 history、表单保存发布 / 双向再编辑 | FormDesigner + shared history | T05、T06、T09 | 已实现；K03 |

## 流程设计器与查看器

参考 `V/features/flow/`、`V/features/designer/`、`V/views/DesignerView.vue`。

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| F01 | Toolbar、版本 / 启停状态、流程 / 表单 / 发起设置页签保持未保存状态 | T06、T09 | 已实现；K04 |
| F02 | NodePalette 8 类型，点击 / 拖入画布，节点 ID 与默认配置 | T06、T09 | 已实现；K04 |
| F03 | 节点拖动、坐标转换 / 测量、zoom / pan、网格、Fit View | T06、T09 | 已实现；K04 |
| F04 | 节点 / 边选中、Inspector 切换、空白取消、删除连带边、Start 不可删 | T06、T09 | 已实现；K04 |
| F05 | checkConnection/applyConnection：自身 / End 出边 / Start 入边 / 重复分支拒绝，普通出边替换 | T06、T09 | 已实现；K04 |
| F06 | 条件边优先级 / 表达式 / 默认标签，默认分支唯一切换，顺序不变 | T06、T09 | 已实现；K04 |
| F07 | 节点插入边 A→N→B，保留条件 / 数组顺序、Condition 默认后继、已连接节点不乱插 | T06、T09 | 已实现；K04 |
| F08 | 自动布局、插入让位、坐标保存与双向读取 | T06、T09 | 已实现；K04 |
| F09 | history 撤销 / 重做、容量 / 输入合并、Ctrl/Cmd+Z / Shift+Z；覆盖节点、边、布局、表单、settings、通知、Action | T06、T09 | 已实现；K04 |
| F10 | NodeInspector 名称 / 类型能力、Any / All / Sequential、空审批人策略、return / transfer / add-sign / comment / qualified-assignee | T06、T09 | 已实现；K04 |
| F11 | ApproverRulesEditor 用户 / 角色 / 人员字段 / 组织层级 / 元数据扩展，值配置与 host resolver 一致 | T06、T09 | 已实现；K04 |
| F12 | NotificationRulesEditor 流程级 / 节点级 events、收件人、channels、title / content / 模板变量 | T06、T09 | 已实现；K04 |
| F13 | TimeoutEditor 提醒 / AutoApprove / AutoReject、minutes / repeat / max，显式允许边界 | T06、T09 | 已实现；K04 |
| F14 | FieldPermissionsEditor Editable / Hidden、节点类型限制及字段引用 | T05、T06、T09 | 已实现；K04 |
| F15 | FlowSettingsPanel startable、category、description、initiatorRoles、titleTemplate 变量 | T06、T09 | 已实现；K04 |
| F16 | 本地补充检查 + 远程 validate，错误 / warnings、非法定义不得发布 | T06、T09 | 已实现；K04 |
| F17 | FlowViewer / Preview 节点图标、状态 / 最近轮次、高亮 / 已访问边、布局 / 缩放 / 平移，查看态不可编辑 | T06、T09 | 已实现；K04 |
| F18 | 全流程 / 分支 / 单节点模拟及节点内步骤定位、上游输出、人工阻塞 / 失败 / Async 计划；输入 / 身份 / 关闭变更废弃旧结果 | T06、T09 | 已实现；K04 |
| F19 | 保存草稿 / 发布说明 / 远程 revision 冲突、历史 `?from=`、未保存路由离开 / 浏览器关闭、模拟不保存草稿 | T02、T06、T07、T09 | 已实现；K04 |

| ID | 节点类型 | 必须保留的能力 | 测试 | 状态 |
| --- | --- | --- | --- | --- |
| N01 | Start | startNodeId、不可删除、next、返回发起人 / Resubmit 展示 | T06、T08、T09 | 已实现；K04 |
| N02 | Approval | 三种审批模式、规则、超时、权限、动作选项 | T06、T08、T09 | 已实现；K04 |
| N03 | Cc | 接收人、隐藏字段、抄送历史 / 逐人阅读 | T06、T08、T09 | 已实现；K04 |
| N04 | Condition | branches priority / expression / next、默认分支 | T06、T09 | 已实现；K04 |
| N05 | Parallel | 全部分支、多出边、嵌套路径与汇聚一致 | T06、T08、T09 | 已实现；K04 |
| N06 | Join | 汇聚与后继、并行 / 嵌套 / 退回路径，由后端校验执行 | T06、T08、T09 | 已实现；K04 |
| N07 | End | 无后继、终态 / 完成展示 | T06、T08、T09 | 已实现；K04 |
| N08 | Action | 顺序步骤、单后继、完整配置 / 输出 / 失败 / 预演 | T06、T09 | 已实现；K04 |

## Action 全部配置

参考 `V/features/designer/Action*.vue`、`actionMappingModel.ts`，配置能力以 `/meta.actions` 和后端 Handler 为准。

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| A01 | 描述符驱动 type/configVersion/supportedModes/allowsContinue/configSchema；没有 Handler 时说明原因 | T06、T09 | 已实现；K05 |
| A02 | 步骤增删、名称 / ID、复制清空 outputKey、拖拽 / 上下移动、撤销重做 | T06、T09 | 已实现；K05 |
| A03 | Sync / Async、Fail / Continue / Retry 允许组合、切模式规则、唯一 outputKey 与上游可见范围 | T06、T09 | 已实现；K05 |
| A04 | JavaScript：CodeMirror 6、行号 / 高亮 / 折叠 / 缩进 / 主题、上下文插入 / 提示、语法错误、格式化、长度、destroy | T06、T09 | 已实现；K05 |
| A05 | Script 保存 / 失焦 / 节点切换 / publish 不丢最新文本，编辑器与流程快捷键不双重撤销 | T06、T09 | 已实现；K05 |
| A06 | Http：登记 endpoint、method、timeoutSeconds、query、headers、body / form；宿主安全约束，不引入任意 URL / 凭证 | T06、T09 | 已实现；K05 |
| A07 | Sql：dataSource、匹配 queryId、parameters、参数类型 / maxRows / timeout；换源清理旧 query / 参数，不能输入 SQL | T06、T09 | 已实现；K05 |
| A08 | Mapping：source / target、Copy/String/Number/Boolean/Date/Array/Object、default/omitNull/precision | T06、T09 | 已实现；K05 |
| A09 | Mapping 嵌套 items、数组 filterPath/filterEquals、规则增删 / 编辑 / 预览、类型保留 | T06、T09 | 已实现；K05 |
| A10 | Notification：接收人全部合法类型、User/Users 字段、channels、title/content 与变量，验证规则 | T06、T09 | 已实现；K05 |
| A11 | 宿主自定义：登记实际 type、schema 生成输入 / 默认 / 枚举 / required，复杂 JSON fallback、未知 config 属性保留 | T06、T09、T10 | 已实现；K05 |
| A12 | ActionJsonEditor：object/array 正确类型、合法 JSON、clearable / required、错误时不写坏 config | T06 | 已实现；K05 |
| A13 | 单节点预演及步骤定位沿可达上游累积合成输出，人工阻塞 / 分支 / 上游失败 / 无 preview 能力说明 | T06、T09 | 已实现；K05 |
| A14 | 上下游输出及 form/outputs/workflow.form/workflow.outputs 路径、同名字段、数组索引、JSON 值保留类型 | T06、T09 | 已实现；K05 |
| A15 | 真后端 JavaScript→Sql→Mapping→审批→Http / Notification + 自定义 Handler；失败策略、审计 / 记录、Webhook 503 / Outbox 重试 | T09、T10 | 已实现；K05 |

## 定义治理

参考 `V/features/definitions/`、`V/features/designer/useDefinitionGovernance.ts`。

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| G01 | 列表名称 / key / 分类 / 当前版本 / 启停 / 草稿 / 更新时间及刷新 | T07、T09 | 已实现；K06 |
| G02 | 服务端分类列表、指定 / 未分类筛选，保留权限收窄 | T07、T09 | 已实现；K06 |
| G03 | NewDefinitionDialog 名称 / key / 重复检查，进入真实设计草稿 | T07、T09 | 已实现；K06 |
| G04 | 保存草稿 expectedRevision/sourceDefinitionId、原键恢复和远程冲突 | T02、T07、T09 | 已实现；K06 |
| G05 | 发布草稿、changeNote、不可变新版本、结果未知后先核对 | T02、T07、T09 | 已实现；K06 |
| G06 | 启用 / 停用、expectedRevision、原因与资源能力 | T07、T09 | 已实现；K06 |
| G07 | 复制已选来源、targetKey / name、稳定键 | T07、T09 | 已实现；K06 |
| G08 | 导出既有 definition package 格式、文件名、鉴权 | T01、T07、T09 | 已实现；K06 |
| G09 | 导入大小 / 格式、预检 / 错误 / 版本、目标变化与迟到响应、确认导入原键恢复 | T02、T07、T09 | 已实现；K06 |
| G10 | 版本列表 / 详情、历史版本 `?from=` 编辑与资源读取权限 | T07、T09 | 已实现；K06 |
| G11 | diff 新增 / 删除 / 修改、交换版本 / 定位变化 / 字段路径，选用版本与 changeNote；比较不改变活动版本 | T07、T09 | 已实现；K06 |
| G12 | 治理审计分页 / 内容、错误 / 空态，409 后刷新而不覆盖草稿 | T07、T09 | 已实现；K06 |

## 监控、Outbox、诊断与消息

| ID | Vue 来源 / 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| O01 | MonitorView：全部流程 / 统计 / 离职交接按各自能力显示和切换 | T07、T08 | 已实现；K07 |
| O02 | MonitorInstances：真实分页、关键词 / 状态 / 流程 / 超期 / 时间、详情 / 终止 | T07、T08 | 已实现；K07 |
| O03 | MonitorStatistics：API 支持的维度 / 时间 / 流程、数量 / 状态 / 超期；无虚构指标 | T07、T08 | 已实现；K07 |
| O04 | HandoverPanel：用户任务预览、接收人 / 原因、批量结果、partial retry 使用同键及 retainOnError | T02、T07、T08 | 已实现；K07 |
| O05 | Outbox：列表 ID / target / node / description / status / attempts / maxAttempts / nextAttemptAt / lastError / createdAt | T07、T09 | 已实现；K07 |
| O06 | 状态 / 实例过滤、分页 URL、3 秒刷新、卸载 / 身份清理、错误 / 空态 | T01、T03、T07 | 已实现；K07 |
| O07 | 人工 retry 权限 / 非成功态、稳定键与 CommandRecovery，刷新结果 | T02、T07、T09 | 已实现；K07 |
| O08 | Payload 独立 API 及敏感授权；普通列表不含正文；宿主 outboxPanel | T07、T08、T09、T10 | 已实现；K07 |
| O09 | DiagnosticsPanel：运行 / 健康、摘要、异常、失败投递、超期与风险分类，真实刷新 | T07、T08 | 已实现；K07 |
| O10 | 诊断到实例 / Outbox 导航和按能力处理，普通错误 / 空态 | T07、T08 | 已实现；K07 |
| O11 | MessageBell：未读数、列表 / 时间 / 内容、10 秒数量刷新、打开加载 / 空 / 错误 | T01、T03、T08 | 已实现；K07 |
| O12 | 单条 / 全部已读原键、成功刷新、跳转详情、失败不伪装已读 | T02、T03、T08 | 已实现；K07 |
| O13 | 用户切换清空消息 / 数量、旧响应隔离、卸载取消 timer / 请求 | T01、T03、T08 | 已实现；K07 |

## 权限：服务端为唯一权威

以 `src/Approval.Core/Abstractions/ApprovalPermissions.cs` 及各资源 capabilities 为依据，不创建 React 私有权限表。每行需要允许与拒绝用例；能力缺失 / 服务不可用默认拒绝。

| ID | 权限范围与验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| P01 | session.read / directory.read：用户及目录可用性，未经授权不推断组织 / 角色 | T01、T08 | 已实现；K08 |
| P02 | instance.read / read-all / read-sensitive：公开实例和敏感投影、任务 / 审计 / 附件隐藏 | T04、T08 | 已实现；K08 |
| P03 | instance.start / withdraw / terminate：资源状态及 canWithdraw，与 menu capability 区分 | T04、T08 | 已实现；K08 |
| P04 | task.read / approve / reject / return：当前 my-task、节点 allowReturn / requireComment | T04、T08 | 已实现；K08 |
| P05 | task.transfer / add-sign / resubmit：节点开关、任务类型、资格限制 | T04、T08 | 已实现；K08 |
| P06 | task.reassign / reassign-bulk：待办资源能力、批量预览 / 部分结果 | T04、T07、T08 | 已实现；K08 |
| P07 | definition.read / read-sensitive：列表、完整编辑 JSON 与实例公开定义不同用途 | T06、T07、T08 | 已实现；K08 |
| P08 | definition.design / validate / simulate / publish / disable：独立能力与治理响应，不能因能读取就能发布 | T06、T07、T08 | 已实现；K08 |
| P09 | draft.read / write / delete、file.read / upload：用户所属与字段 / 附件访问策略 | T05、T08 | 已实现；K08 |
| P10 | message.read / write：消息读写不越权，抄送已读仍按个人 | T03、T08 | 已实现；K08 |
| P11 | outbox.read / retry + instance.read-sensitive 与实例访问策略：OutboxRead 不赋予 Payload 权限 | T07、T08 | 已实现；K08 |
| P12 | statistics.read / diagnostics.read 各自授权；system.outbox-dispatch / system.task-timeout 不作为 HTTP 人工能力开放 | T07、T08 | 已实现；K08 |

## 所有页面的错误、空态与数据隔离

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| E01 | 首次加载 / 刷新 / mutation busy 与 disabled，不重复提交 | T01–T09 | 已实现；K08 |
| E02 | 四工作台、流程 / 草稿 / 版本 / 目录 / 消息 / 抄送 / Action / Outbox / diagnostics 无数据提示 | T03–T09 | 已实现；K08 |
| E03 | 401 调宿主 onUnauthorized；403 / AuthorizationUnavailable 默认拒绝，隐藏旧敏感数据 | T01、T08 | 已实现；K08 |
| E04 | 404 无权查看 / 不存在均正确处理，不回显旧实例；路由参数消失不发新请求 | T03、T04、T08 | 已实现；K08 |
| E05 | 400 ValidationProblemDetails 字段路径、保留输入；未知非 JSON HTTP 错误可读提示 | T01、T04、T05 | 已实现；K08 |
| E06 | 409 状态 / revision / 幂等内容冲突，刷新最新状态，不无条件自动重试或换键 | T02、T04、T07、T08 | 已实现；K08 |
| E07 | 网络 / 5xx / CommitOutcomeUnknown 保留原意图与恢复提示；普通读请求可刷新 | T01、T02、T08 | 已实现；K08 |
| E08 | AbortError 不当成业务失败；A→B→A、卸载、输入 / 路由变化的迟到响应与错误均忽略 | T01、T03、T06、T08 | 已实现；K08 |
| E09 | 文件 / JSON / 公式 / handler 配置格式错误、不支持 schemaVersion、无 preview / 目录 501 的实际说明 | T05、T06、T09 | 已实现；K08 |
| E10 | 双 Provider、刷新、StrictMode 清理；敏感正文 / API 响应 / Token 不进浏览器持久存储或日志 | T01、T02、T08、T10 | 已实现；K08 |

## 宿主集成、布局与设计系统

| ID | 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| H01 | ApprovalProvider 全选项：apiBase/headers/fetcher/onUnauthorized/userKey/businessPanels/outboxPanel | T01、T10 | 已实现；K09 |
| H02 | useApprovalClient/createApprovalClient 独立实例、清理 / 所有权与 Provider 生命周期 | T01、T10 | 已实现；K09 |
| H03 | useApprovalUser：me/userId/roles/capabilities/can/isAdmin/canViewDefinitions/formsEnabled/formBusinessType/reload | T01、T08 | 已实现；K09 |
| H04 | 身份变化宿主触发 React 渲染、同步清空与请求隔离；两个 Provider 配置 / 缓存独立 | T01、T08、T10 | 已实现；K09 |
| H05 | createApprovalRoutes：9 路由、相对宿主挂载、懒加载、菜单能力 / 子路由高亮 / 响应式导航 | T03、T08、T10 | 已实现；K09 |
| H06 | BusinessType 对应 React 面板、businessKey、未知业务展示、无 Forms 宿主；Certificate 不在库 | T04、T08、T10 | 已实现；K09 |
| H07 | Outbox 扩展面板、UserPicker 可定制适配和公开类型 | T05、T07、T10 | 已实现；K09 |
| H08 | 组件 / API / Flow / Forms / Panel / client 类型及纯函数导出，d.ts 不依赖内部源码 alias | T10 | 已实现；K09 |
| H09 | shared 和 React tgz 独立安装 / typecheck / build / browser，JS / 声明无 Vue / EP，依赖只通过公开 exports | T01、T10 | 已实现；K09 |
| H10 | 独立 dev/build/lib/test/typecheck React 脚本、共享构建依赖、原 Vue 入口稳定与 CI 纳入 | T10、Vue 原有门禁 | 已实现；K09 |
| U01 | 统一基础 UI：输入 / 选择 / 日期 / 上传 / 分页 / Table / FormField，loading / error / disabled | T05、T10 | 已实现；K09 |
| U02 | Radix Dialog/AlertDialog/Drawer/Popover/Menu/Tabs/Tooltip/Toast，键盘、焦点恢复、aria、ConfirmDialog | T04、T10 | 已实现；K09 |
| U03 | EmptyState/Loading/Skeleton/ErrorState，桌面 / 平板 / 手机、可长时间使用的画布 | T08–T10 | 已实现；K09 |
| U04 | Tailwind v4 CSS-first、主题变量、统一间距 / 字体 / 色彩 / 圆角，打包 CSS 和宿主隔离 | T10 | 已实现；K09 |

## React Demo

| ID | Vue 来源 / 验收点 | 测试 | 状态 |
| --- | --- | --- | --- |
| C01 | D/stores/session.ts、UserSwitcher：真实用户 API、X-User-Id、approval-demo-user、身份切换 | T01、T08 | 已实现；K10 |
| C02 | D/layouts/AppLayout.vue：品牌、用户 / 角色、消息、能力菜单、高亮与移动导航 | T03、T08 | 已实现；K10 |
| C03 | D/views/CertificatesView.vue、CertificateTable：列表 / 查询 / 分页 / 状态 | T08 | 已实现；K10 |
| C04 | CertificateFormDialog、submissionIdentity：申请、编辑 / 提交、稳定 UUID、业务与审批原子结果 | T02、T08 | 已实现；K10 |
| C05 | CertificateRequestPanel、CertificateHistoryDrawer：业务详情 / 历史 / 审批关联 | T08 | 已实现；K10 |
| C06 | RevokeDialog、CertificateDiagnosticsPanel：撤销确认 / 授权 / 诊断 | T08 | 已实现；K10 |
| C07 | DemoWebhookPanel：接收记录 / Idempotency-Key、下一轮失败次数、503、自动 / 人工 retry、成功去重 | T09 | 已实现；K10 |
| C08 | React dev 5174、proxy 5199、独立 dist；与 Vue 同时运行互不覆盖构建、Demo 业务不反向依赖通用库 | T10 | 已实现；K10 |

## 实际证据索引

K01–K10 覆盖前文全部 178 个编号。以下路径相对仓库根目录；用例名称是源文件中的实际名称。详细结果、命令和输出目录见 [验收报告](../../../../docs/react-frontend-report-2026-11-06.md)。共享纯算法仍通过原 Vue 用例验证；后端授权 / 并发 / 分支 / 事务由原后端测试验证，并未移植第二套引擎。

| 索引 / 编号 | 实际实现 | 精确测试 / 证据 |
| --- | --- | --- |
| K01：W01–W06 | R/pages/WorkbenchPage.tsx、R/hooks/usePagedList.ts、R/features/workbench/ListFilterBar.tsx；shared/features/lists/listModel.ts | `restores prefixed URL pagination and filters on load and history navigation and refreshes an unchanged search`；`React monitor performs real bulk handover and diagnostics and carbon copy reads stay per user`；原 Vue usePagedListRouting / usePagedList 用例 |
| K02：D01–D18、S01–S08 | R/pages/ApprovalDetailPage.tsx、StartCenterPage.tsx、StartFormPage.tsx；R/features/approval/ApprovalActions.tsx、ApprovalHistory.tsx；shared/commands/commandIntent.ts | `React saves and restores a draft submits a form and recovers an approval after the committed response is lost`；`React rejects with a required reason and records one terminal rejection without altering form values`；`React returns an approval restores the original values and resubmits the edited form`；`React add-sign and transfer use real directory users and preserve the original approval sequence`；`React After add-sign keeps the required approval participants`；`React Parallel add-sign keeps the required approval participants`；`React withdraw terminate and administrator reassign persist their real instance outcomes` |
| K03：FV01–FV16、FM01–FM10、FD01–FD09 | R/features/forms/DynamicForm.tsx（包括明细表、值显示）、FormDesigner.tsx（包括字段 / 选项 / 公式 / 列编辑）、FileInput.tsx；R/components/UserPicker.tsx；shared/features/forms/*.ts | `renders all sixteen schema field types without changing input values`；`validates nested detail errors switch required precision date order and user limits`；`adds and deletes Table rows with six supported column types and computes row formulas and totals`；`formats readonly Money and Formula with Vue precision and summarizes tables and empty selections`；`passes readonly and editable user states to the host picker without changing ID types`；`discards an upload that finishes after unmount and performs no automatic retry after a storage failure`；原 Vue formula / formModel / formFormulas / formValidation 用例；双向完整 JSON 用例 |
| K04：F01–F19、N01–N08 | R/pages/DesignerPage.tsx；R/features/flow/FlowCanvas.tsx、FlowViewer.tsx；R/features/designer/designerModel.ts、RuleEditors.tsx、DesignerSimulation.tsx；R/features/forms/FlowSettingsPanel.tsx；shared/features/flow/*.ts、history.ts | `React canvas connects approval nodes saves a real draft and Vue can edit the same definition`；`React palette drop inserts on a conditional edge and undo redo preserves its default branch and ordering`；`staggers click-added nodes like Vue and places palette drops at the exact requested center`；`converts timeout and repeated reminders to integer minutes while preserving maximum reminders`；`React previews a JavaScript action with synthetic data and leaves draft and external systems untouched`；原 Vue flowGraph / flowRuntime / flowLayout / history / useDefinitionSimulation 用例 |
| K05：A01–A15 | R/features/designer/ActionStepsEditor.tsx、ActionConfigEditor.tsx、ActionScriptEditor.tsx、ActionMappingEditor.tsx；shared/features/designer/actionModel.ts、actionMappingModel.ts | `React and Vue preserve all field node and Action configuration on both editing directions`；`React publishes every Action and shared backend retries HTTP 503 with one stable execution key`；`uses host action descriptors for custom versions defaults modes and no new protocol fields`；`JsonEditor rejects malformed or wrong-type JSON without mutating its model` |
| K06：G01–G12 | R/pages/DefinitionsPage.tsx；R/features/definitions/ImportDefinitionDialog.tsx、DefinitionVersionsDialog.tsx；shared/features/definitions/*.ts | `React definition governance creates publishes compares selects versions and imports a real export`；`missing keys forbidden editable fields stale revisions and identity changes cannot produce partial writes`；原 Vue definitionDiff / definitionTransfer / useDefinitionGovernance 用例 |
| K07：O01–O13 | R/pages/MonitorPage.tsx、OutboxPage.tsx、DiagnosticsPage.tsx；R/features/monitor/MonitorPanels.tsx；R/components/MessageBell.tsx | `React monitor performs real bulk handover and diagnostics and carbon copy reads stay per user`；`marks only unread messages once and navigates already-read messages without another write`；`defaults message write permissions to denied and clears notifications when their owner changes`；真实 Action Outbox 503→200；Payload 使用既有敏感 API，不在列表生成正文 |
| K08：P01–P12、E01–E10 | 各页面资源 capabilities；R/client.tsx、R/hooks/useAsyncData.ts；shared/client.ts、api/http.ts、commands/commandIntent.ts | `missing keys forbidden editable fields stale revisions and identity changes cannot produce partial writes`；`denies missing permissions even when a task is assigned and displayed`；`disables an open approval dialog when its resource permission is revoked`；`ignores an old response and error after route inputs change even when the loader ignores cancellation`；`retains frozen payload and original key after an unknown commit and a later forbidden replay`；原后端权限、幂等、并发与事务负向用例 |
| K09：H01–H10、U01–U04 | R/client.tsx、routes.ts、index.ts、styles.css、components/ui/*.tsx；eng/verify-react-package.mjs；web/README.md；docs/react-integration.md、react-design-system.md | `keeps two owned Providers independent through StrictMode and identity changes with Forms disabled`；`Dialog receives focus traps keyboard navigation and closes on Escape`；`Command supports keyboard selection and returns the selected value`；`Calendar navigates months and keeps date boundaries disabled`；真实 tgz 安装 / JS / d.ts / CSS / native ESM / 依赖检查与 packed 浏览器；响应式源码与 production 截图审阅 |
| K10：C01–C08 | RD/main.tsx、session.ts、pages/CertificatesPage.tsx、features/CertificateFormDialog.tsx、CertificateRequestPanel.tsx、CertificateDiagnosticsPanel.tsx、DemoWebhookPanel.tsx；web/demo-shared | `React certificate Demo creates a real request completes three approval stages and shows certificate history`；`React publishes every Action and shared backend retries HTTP 503 with one stable execution key`；两套 Demo 构建和 submissionIdentity 用例 |

测试文件：`web/packages/approval-shared/tests/client.test.ts`、`web/packages/approval-react-ui/tests/**`、`web/apps/react-demo/tests/**`、`web/tests/browser/react/*.spec.ts`；原 Vue 用例继续全量运行，未删除或跳过。178 个编号不是 178 个独立自动化场景；CodeMirror 长时间编辑、所有移动设备和每个权限组合没有逐一浏览器穷举。

## 共同协议边界

| 请求文字 | 当前协议与实现 |
| --- | --- |
| 正则校验 / 通用联动 | Schema 没有该配置；保留原验证、公式与汇总联动，不新增 React 私有字段 |
| 明细表所有字段 | 当前 6 类：Text、Number、Money、Date、Select、Formula |
| HTTP 地址 / 响应映射 | 登记 endpoint；同步 GET 仍支持 statusCode/body；预演仅请求计划，没有新响应映射字段 |
| SQL 原始文本 / 输出映射 | 登记只读查询和 parameters；输出转换使用后续 Mapping |
| Mapping 任意表达式 | 保留声明式 transform/default/omitNull/precision/items/filter；任意表达式未列入协议 |
| 六种固定 Action | 5 个内置 Handler + 宿主动态登记 Handler；测试 HostEcho 为 configVersion=3 |
| 单步骤独立执行 | API 的 ruleIndex 是审批人规则索引；Action 按节点顺序预演并定位步骤，不伪造独立执行协议 |
| 抄送接收成员列表 | 该接口返回目录 / 角色成员，阅读状态按当前用户抄送查询；不会虚构成员 readAt |

本地服务数据库矩阵未运行；Model Drift 只验证模型。无 npm 发布、Git 提交、推送或 SHA 对应的 CI 验收。
