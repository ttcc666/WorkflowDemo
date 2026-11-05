# 流程定义导入和导出

在「流程定义」的「版本与治理」中，从某个已发布版本选择「导出」。文件由该版本的配置生成，包含审批人规则、通知配置和可选表单快照。读取需要 `DefinitionReadSensitive`。文件中的 Resolver 参数、用户或角色标识与渠道名称可能依赖源宿主，请在目标宿主检查可用性。

在目标宿主打开「流程定义」，选择「导入流程定义」。选择文件并填写未占用的 Key，点击「检查文件和宿主依赖」，通过后选择「导入为草稿」。预检检查定义结构、已注册的 Resolver、已声明的通知渠道和可选 Forms 能力。导入会重新执行检查，并在事务内创建未发布草稿和治理审计。打开草稿确认目标宿主配置，完成设计后用现有发布入口手动发布。

文件是单个 UTF-8 `.approval.json`，导入上限为 1 MiB。文件包的 `packageVersion` 和 `definition.schemaVersion` 当前都是 1，分别标识外层文件格式和流程结构。`kind` 固定为 `approval-definition`。`source` 保存原 Key、名称、发布版本和 UTC 发布时间，仅用于标识来源。包中 `definition` 的字段遵守流程 Schema。历史大文件可以导出，导入前需在设计器中调整定义大小。 [文件 Schema](../contracts/definition-package.schema.json) · [流程 Schema](../contracts/flow.schema.json)

默认 HTTP 前缀为 `/api/approval`。`GET /definitions/by-id/{id}/export` 返回文件 JSON。`POST /definitions/{key}/import/validate` 和 `POST /definitions/{key}/import` 使用 `{"packageBase64":"…"}` 携带原始文件字节，请求体上限为 1.5 MiB。两个操作都需要目标 Key 的 `DefinitionDesign`，提交还要求 `Idempotency-Key`。预检不占用目标 Key。目标已有设计草稿或发布版本时，提交返回冲突。

断线或 `CommitOutcomeUnknown` 时，使用原 Key、原文件和原 `Idempotency-Key` 恢复。界面可在当前页面重试原请求，重新进入后可选择原文件并输入同一个目标 Key 恢复。成功后的现有草稿读取与发布仍按目标流程的宿主授权判断。
