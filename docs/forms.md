# 可选 Forms

安装 Approval.Forms 与 Approval.Forms.AspNetCore，调用 AddApprovalForms、AddApprovalFormsAspNetCore，并在模型调用 ConfigureApprovalForms。只安装 Core/AspNetCore 时不需要这些类型、表或服务。

Forms 保留 Text/Number/Date/Select/Radio/Switch/User/Users/Table/Formula/File、字段权限、设计器数据、渲染契约、校验、规范化、业务草稿和版本快照。公式仍由服务器计算；修改字段仍先校验审批权限，再规范化、校验附件、追加变更审计，并与任务同一事务提交。

Core 的 FlowDefinition.Form 是可选模块拥有的 JSON 数据。Forms 通过 GetForm 解码并缓存强类型快照；图结构校验属于 Core，表单、人员字段及公式校验属于 Forms。未安装模块时包含动态表单快照的定义会明确拒绝，防止跳过校验。

附件元数据/引用属于 Forms，实际内容归宿主 IApprovalFileStore。默认缺失适配器只在实际文件操作时报错，纯字段表单不要求存储。LocalApprovalFileStore 由宿主显式选择，不能放在公开静态目录。没有未绑定文件定时清理、删除任务或保留周期。

前端当前继续使用 @approval/ui 中的表单组件；拆为独立 @approval/forms 是后续前端阶段，不能把后端独立性等同于前端已拆包。

Draft/File 写权限仅供 Forms 内部服务使用，与 Core 引擎、定义治理、消息已读权限分开。普通宿主不能通过共享 DbContext 或 bulk 修改元数据。附件绑定仍与草稿/审批事务一起保存；上传元数据保存失败会撤销跟踪，避免之后保存失去文件内容的残留记录。

FlowNode.FieldPermissions、FieldAccess、FieldsWith 和 HiddenFields 是当前字段权限功能使用的数据结构。校验、字段隐藏、编辑规则和附件可见性由 Forms 扩展实现；缺少模块时拒绝相关定义/投影。它们没有旧版本适配分支，不因清理兼容层而删除。流程发布版本及实例绑定仍保持不可变；外部定义 JSON 必须显式声明 schemaVersion=1。
