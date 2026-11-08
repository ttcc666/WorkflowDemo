# React 测试质量审查

本轮使用 code-testing 的 Research → Plan → Implement 流程，并用 assertion-quality / test-gap-analysis 审查新增断言。内部中间状态位于 Git metadata 的 testagent 目录，不是交付文件。

审查范围：shared transport/client/intent、React hooks、全部表单输入、designer model、审批动作和真实 Playwright 用例。沿用所有原 Vue 回归，未删除、skip 或放宽旧测试。

## 已修复的审查发现

- 同步 loader 错误用例原先使用 async 函数，只能证明 Promise rejection；改为真实同步 throw，防止移除 Promise 包装后仍虚假通过。
- Dialog 关闭焦点原先落到 body；增加受控 Dialog 触发焦点恢复并保持明确 focus 断言。
- 审批动作原先误用 task 列表能力；改为 my-task 顶层资源能力，并新增缺失默认拒绝 / 权限撤销 / busy / requireComment 断言。
- 单个 SVG 垂直边 bounding box 宽度为 0；拖拽用真实路径屏幕坐标定位到画布，不通过测试内部模型直接改图。保持保存后的分支、顺序、后继和两步撤销重做断言。
- Action ruleIndex 原先当作步骤序号；按后端协议预演节点并定位步骤，验证固定 JS 输出、错误展示、无草稿变化、无外部调用。
- 只判断请求成功不足以证明幂等：失联审批检查两次键 / 正文完全相同、最终 amount / 状态和恰好一次 Approve 日志。
- Action 记录的普通读取会脱敏；分别断言普通人 details=null、管理员可读取实际类型，避免用提高权限掩盖脱敏错误。
- 完整组合运行暴露点击新增节点没有 Vue 的错位偏移，审批节点尺寸超出共同图算法的 72px，导致手柄重叠。恢复错位、固定节点尺寸，并在真实连线后等待对应边出现；保存后的 start→approval→end 断言保留。
- 明细表用例名称要求六类，初始 fixture 只有三类；补全 Number / Date / Select，验证六类新行默认值，保留行公式和顶层汇总的精确对象断言。
- 增加只读金额 / 公式精度、宿主人员选择器禁用及大小写字段权限、上传卸载迟到结果、失败不自动重试、URL 首次恢复 / 历史导航 / 相同搜索刷新、消息只对未读项写入及身份清理、双 Provider / StrictMode / 无 Forms 回归。
- SQLite 自动 Worker 与 HTTP 写入同时发生时，曾出现真实的 30 秒锁等待；20 秒 Outbox 断言窗口不足。改为 65 秒有界轮询和 90 秒该场景超时，仍严格断言 503→200、原键、原正文和最终状态；不更改生产 Worker、事务或重试语义。
- 最终 typecheck 发现新增 Testing Library 用例误用了 Playwright 的 `exact` 选项，以及分页 fixture 缺少协议要求的 page/pageSize；修正后源码 / 测试类型检查通过，保留完整协议类型而不使用类型强制转换绕过检查。
- 发布声明审查发现根 index.d.ts 引用了未发布的 styles.css；建立仅构建使用的 library.ts 收集样式，保持公开类型入口纯净，并在真实包消费者中检查根声明、CSS 内容和 Vue / React / shared 版本一致性。
- 补充非根路径宿主验证：消息详情导航从审批布局使用当前布局的相对路径，保留 /host/workflow；全局消息组件可用 detailPath 指定目标。根路径和嵌套路径均有具体地址断言。
- 源码完整 E2E 发现任务先于表单定义返回时，同意按钮可点击，但验证 ref 尚未安装导致静默不提交。详情动作在任务 / 定义加载及可编辑表单未就绪时禁用；新增显式阻挡 definition 响应的浏览器回归。身份切换前明确等待动作对话框关闭，保留实际状态 / 审计断言。
- 实际 tgz 浏览器发现 React Flow 依赖中的 CommonJS require(react) 在库构建中保留，导致浏览器崩溃；consumer build 成功并不能证明可运行。库对已声明 React Flow / Radix / CodeMirror 等 JS 依赖保留公开 imports，由宿主应用完整构建，CSS 继续收集。重跑真实打包浏览器，保持双向设计器与审批用例而不绕过发布包。
- 最后复核 Vue 的独立 ReassignDialog：改派排除发起人 / 原处理人，原因选填；React 通用动作窗口曾误用了转交的排除集及必填原因。按 Vue 修正，并直接验证管理员可选、原处理人不可选、空原因可提交，最终资格仍由后端复核。
- 完整解决方案同时运行四个测试项目时，未修改的 P4Safety 并发用例曾耗尽 30 秒取消保护；原样单独执行三种撤权分区，3/3 在 3 秒内通过。验证脚本增加 MSBuild -m:1，按测试项目顺序运行，测试内部并发请求、显式屏障、取消保护和全部断言保持原样；最终完整门禁验证此调度方式。
- 列表 owner 用 API 地址与用户身份比较；StrictMode 的清理会递增请求 generation，但不代表用户变化。新增 owned Provider + StrictMode 首次 URL 页码恢复回归，避免仅借用客户端的测试漏掉该边界；异步请求仍按 generation 隔离。

## 断言维度

包括完整对象 / 数组值和顺序、嵌套配置、数据类型、错误码 / 状态、无部分写入、请求次数 / 参数、原对象不变、用户隔离、可编辑字段范围、UI focus/disabled、真实数据库最终状态、实际发布版本、原键重试及无外部副作用。

具备具体 expected values 的路径包括 formula 3/427、SQL 库存 30、HTTP GET 42、503→200 顺序、三种加签、同键同正文、secret 未改且任务仍 Running、revision 409、两用户 CC readAt 独立和版本选择 / 导入 round-trip。

## Pseudo-mutation 映射（源码与断言审查）

| 假设业务变化 | 对应能发现的证据 |
| --- | --- |
| 重试换键或读取可变原正文 | shared `retains frozen payload...`、浏览器 `React saves and restores a draft...` |
| 旧用户 / 旧路由响应覆盖当前数据 | shared A→B→A 用例、React `ignores an old response and error...`、浏览器权限隔离 |
| 缺能力默认允许 / 打开后撤销权限仍可提交 | `denies missing permissions...`、`disables an open approval dialog...` |
| 金额 / 日期 / 用户 / Boolean 变成不同 JSON 值 | fields.test.tsx 的具体回调值与原对象不变断言 |
| 插入边丢默认分支或顺序、undo 丢配置 | designerModel 对象断言与真实 palette drop 保存 / undo / redo |
| 仅渲染 Action 编辑器而不真实执行 | React 发布 + 后端最终 Approved、完整 Action 日志、库存 / HTTP / HostEcho 输出与自动 retry |
| 导出 / 导入 / 版本选择改变活动历史或遗漏结构 | governance 与双向完整 round-trip 用例 |

本表是 source/assertion 映射，未声称运行了 mutation tool 或测量覆盖率。已有 Backend 988 个测试进一步验证权限、事务、状态机与持久化边界；前端不会复制一套审批引擎。
