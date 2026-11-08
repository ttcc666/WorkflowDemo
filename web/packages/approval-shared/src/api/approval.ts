import { type ResolvedTransportOptions } from '../transportOptions.js'
import { type ApprovalHttpClient } from './http.js'
import type {
  AddSignMode,
  CarbonCopyItem,
  CarbonCopyRecipients,
  ApprovalDefinition,
  DefinitionGovernance,
  DefinitionGovernanceSummary,
  DefinitionAudit,
  DefinitionImportReceipt,
  DefinitionTransferSource,
  ApprovalDiagnosticReport,
  ApprovalInstance,
  ApprovalMe,
  ApprovalMessage,
  ApprovalMeta,
  ApprovalStatistics,
  DirectoryUser,
  DraftDetail,
  DraftSummary,
  FlowSummary,
  InstanceListItem,
  ListQuery,
  MyTaskResult,
  OutboxMessage,
  OutboxPayload,
  PagedResult,
  ReassignResult,
  ReturnTarget,
  StartForm,
  StartableFlow,
  TodoItem,
  ValidationResult,
  ApprovalSimulationResult,
} from '../contracts/api.js'
import type { FileValue, FormValues } from '../contracts/forms.js'

const enc = encodeURIComponent
/** 查询字符串：忽略空值（undefined、null、''、false） */
export function queryString(query: object = {}) {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '' && value !== false) params.set(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

/** API 方法绑定到指定客户端，异步回调不会重新读取其他应用的配置。 */
export function createApprovalApi(
  http: ApprovalHttpClient,
  getConfig: () => Pick<ResolvedTransportOptions, 'apiBase'>,
) {
  const api = (path: string) => getConfig().apiBase + path
  const commandHeaders = (key: string) => {
    if (typeof key !== 'string' || !key.trim()) throw new TypeError('业务写操作需要稳定 OperationId')
    return { 'Idempotency-Key': key }
  }
  return {
    copyDefinition: (key: string, body: { targetKey: string; name: string; sourceDefinitionId?: string }, operationId: string) =>
      http.post<DefinitionGovernance>(api(`/definitions/${enc(key)}/copy`), body, commandHeaders(operationId)),
    me: (signal?: AbortSignal) => http.get<ApprovalMe>(api('/me'), signal),
    diagnostics: (signal?: AbortSignal) => http.get<ApprovalDiagnosticReport>(api('/diagnostics'), signal),
    users: (keyword: string, take = 50, signal?: AbortSignal) =>
      http.get<DirectoryUser[]>(api(`/directory/users?keyword=${enc(keyword)}&take=${take}`), signal),
    usersByIds: (ids: string[], signal?: AbortSignal) =>
      http.get<DirectoryUser[]>(api(`/directory/users?ids=${ids.map(enc).join(',')}`), signal),
    roles: (signal?: AbortSignal) => http.get<string[]>(api('/directory/roles'), signal),

    startableForms: (signal?: AbortSignal) => http.get<StartableFlow[]>(api('/forms/startable'), signal),
    startForm: (key: string, signal?: AbortSignal) => http.get<StartForm>(api(`/forms/${enc(key)}`), signal),
    /** 发起表单审批；校验失败抛出 ApiError（errors 为字段错误）；带 draftId 时提交成功后删除草稿 */
    submitForm: (key: string, formData: FormValues, comment: string, draftId: string | null | undefined, operationId: string) =>
      http.post<{ instanceId: string }>(api(`/forms/${enc(key)}/start`), { formData, comment, draftId: draftId ?? null }, commandHeaders(operationId)),

    drafts: (signal?: AbortSignal) => http.get<DraftSummary[]>(api('/forms/drafts'), signal),
    draft: (id: string, signal?: AbortSignal) => http.get<DraftDetail>(api(`/forms/drafts/${id}`), signal),
    /** 保存草稿（不校验必填）；draftId 为空时新建 */
    saveDraft: (key: string, draftId: string | null, formData: FormValues, comment: string, operationId: string) =>
      http.put<{ draftId: string; updatedAt: string }>(api(`/forms/${enc(key)}/draft`), { draftId, formData, comment }, commandHeaders(operationId)),
    deleteDraft: (id: string, operationId: string) => http.delete(api(`/forms/drafts/${id}`), commandHeaders(operationId)),

    todo: (query: ListQuery = {}, signal?: AbortSignal) => http.get<PagedResult<TodoItem>>(api(`/todo${queryString(query)}`), signal),
    todoCount: (signal?: AbortSignal) => http.get<{ count: number }>(api('/todo/count'), signal),
    done: (query: ListQuery = {}, signal?: AbortSignal) => http.get<PagedResult<InstanceListItem>>(api(`/done${queryString(query)}`), signal),
    started: (query: ListQuery = {}, signal?: AbortSignal) =>
      http.get<PagedResult<InstanceListItem>>(api(`/started${queryString(query)}`), signal),
    /** 抄送我的（含抄送给我所在角色的） */
    cc: (query: ListQuery = {}, signal?: AbortSignal) => http.get<PagedResult<InstanceListItem>>(api(`/cc${queryString(query)}`), signal),
    carbonCopies: (query: ListQuery = {}, signal?: AbortSignal) => http.get<PagedResult<CarbonCopyItem>>(api(`/carbon-copies${queryString(query)}`), signal),
    carbonCopyRecipients: (id: string, page = 1, pageSize = 20, signal?: AbortSignal) =>
      http.get<CarbonCopyRecipients>(api(`/carbon-copies/${id}/recipients${queryString({ page, pageSize })}`), signal),
    readCarbonCopy: (id: string, operationId: string) => http.post(api(`/carbon-copies/${id}/read`), {}, commandHeaders(operationId)),
    carbonCopyHistory: (id: string, signal?: AbortSignal) => http.get<CarbonCopyItem[]>(api(`/instances/${id}/carbon-copies`), signal),
    /** 已发布流程的标识与名称（筛选下拉） */
    flows: (signal?: AbortSignal) => http.get<FlowSummary[]>(api('/flows'), signal),

    instance: (id: string, signal?: AbortSignal) => http.get<ApprovalInstance>(api(`/instances/${id}`), signal),
    /** 实例绑定版本的流程定义（公开视图：不含通知与时限配置），用于画流程图、展示表单 */
    instanceDefinition: (id: string, signal?: AbortSignal) => http.get<ApprovalDefinition>(api(`/instances/${id}/definition`), signal),
    byBusiness: (businessType: string, businessKey: string) =>
      http.get<ApprovalInstance[]>(api(`/instances?businessType=${enc(businessType)}&businessKey=${enc(businessKey)}`)),
    myTask: (instanceId: string, signal?: AbortSignal) => http.get<MyTaskResult>(api(`/instances/${instanceId}/my-task`), signal),

    /** formData 仅表单审批可传：只含任务所在节点的可编辑字段，同意时一并修改 */
    approve: (taskId: string, comment: string, formData: FormValues | undefined, operationId: string) =>
      http.post(api(`/tasks/${taskId}/approve`), { comment, formData: formData ?? null }, commandHeaders(operationId)),
    /** targetNodeId 为空时驳回并结束流程，否则退回到该节点 */
    reject: (taskId: string, comment: string, targetNodeId: string | undefined, operationId: string) =>
      http.post(api(`/tasks/${taskId}/reject`), { comment, targetNodeId: targetNodeId ?? null }, commandHeaders(operationId)),
    returnTargets: (taskId: string, signal?: AbortSignal) => http.get<ReturnTarget[]>(api(`/tasks/${taskId}/return-targets`), signal),
    addSign: (taskId: string, userIds: string[], mode: AddSignMode, comment: string, operationId: string) =>
      http.post(api(`/tasks/${taskId}/add-sign`), { userIds, mode, comment }, commandHeaders(operationId)),
    /** formData 仅表单审批可传（修改后的表单） */
    resubmit: (taskId: string, comment: string, formData: Record<string, unknown> | undefined, operationId: string) =>
      http.post(api(`/tasks/${taskId}/resubmit`), { comment, formData: formData ?? null }, commandHeaders(operationId)),
    transfer: (taskId: string, toUserId: string, comment: string, operationId: string) => http.post(api(`/tasks/${taskId}/transfer`), { toUserId, comment }, commandHeaders(operationId)),
    withdraw: (instanceId: string, comment: string, operationId: string) => http.post(api(`/instances/${instanceId}/withdraw`), { comment }, commandHeaders(operationId)),

    /** 上传附件（未绑定，提交表单后绑定到申请）：文件名放在请求头，请求体为文件内容 */
    uploadFile: (file: File) =>
      http.upload<FileValue>(api('/files'), file, { 'X-File-Name': encodeURIComponent(file.name), 'Content-Type': 'application/octet-stream' }),
    /** 下载附件（带鉴权请求头）并保存 */
    downloadFile: async (id: string, name: string) => {
      const url = URL.createObjectURL(await http.blob(api(`/files/${id}`)))
      const link = document.createElement('a')
      link.href = url
      link.download = name
      link.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    },

    meta: () => http.get<ApprovalMeta>(api('/meta')),
    definitions: (signal?: AbortSignal) => http.get<ApprovalDefinition[]>(api('/definitions'), signal),
    governanceDefinitions: (signal?: AbortSignal, category?: string, uncategorized = false) => http.get<DefinitionGovernanceSummary[]>(api(`/definitions/governance${queryString({ category, uncategorized })}`), signal),
    definitionCategories: (signal?: AbortSignal) => http.get<string[]>(api('/definitions/categories'), signal),
    definitionGovernance: (key: string, signal?: AbortSignal) => http.get<DefinitionGovernance>(api(`/definitions/${enc(key)}/governance`), signal),
    saveDesignDraft: (key: string, body: { name: string; definitionJson: string; expectedRevision: number; sourceDefinitionId: string | null }, operationId: string) =>
      http.put<DefinitionGovernance>(api(`/definitions/${enc(key)}/draft`), body, commandHeaders(operationId)),
    publishDesignDraft: (key: string, body: { expectedRevision: number; changeNote: string }, operationId: string) =>
      http.post<{ id: string; key: string; version: number }>(api(`/definitions/${enc(key)}/publish-draft`), body, commandHeaders(operationId)),
    setDefinitionEnabled: (key: string, body: { enabled: boolean; expectedRevision: number; changeNote: string }, operationId: string) =>
      http.post<DefinitionGovernance>(api(`/definitions/${enc(key)}/enabled`), body, commandHeaders(operationId)),
    selectDefinitionVersion: (key: string, body: { definitionId: string; expectedRevision: number; changeNote: string }, operationId: string) =>
      http.post<DefinitionGovernance>(api(`/definitions/${enc(key)}/select-version`), body, commandHeaders(operationId)),
    definitionAudit: (key: string, page = 1, signal?: AbortSignal) => http.get<PagedResult<DefinitionAudit>>(api(`/definitions/${enc(key)}/audit?page=${page}`), signal),
    latestDefinition: (key: string) => http.get<ApprovalDefinition>(api(`/definitions/${enc(key)}`)),
    definitionById: (id: string, signal?: AbortSignal) => http.get<ApprovalDefinition>(api(`/definitions/by-id/${id}`), signal),
    exportDefinition: (id: string) => http.blob(api(`/definitions/by-id/${enc(id)}/export`)),
    validateDefinitionImport: (key: string, packageBase64: string) =>
      http.post<DefinitionTransferSource>(api(`/definitions/${enc(key)}/import/validate`), { packageBase64 }),
    importDefinitionDraft: (key: string, packageBase64: string, operationId: string) =>
      http.post<DefinitionImportReceipt>(api(`/definitions/${enc(key)}/import`), { packageBase64 }, commandHeaders(operationId)),
    versions: (key: string, signal?: AbortSignal) => http.get<ApprovalDefinition[]>(api(`/definitions/${enc(key)}/versions`), signal),
    simulate: (definitionJson: string, formData: Record<string, unknown>, nodeId?: string, ruleIndex?: number) =>
      http.post<ApprovalSimulationResult>(api('/definitions/simulate'), { definitionJson, formData, nodeId, ruleIndex }),
    validate: (flowJson: string) => http.postJson<ValidationResult>(api('/definitions/validate'), flowJson),
    publish: (key: string, name: string, flowJson: string, operationId: string) =>
      http.postJson<{ id: string; key: string; version: number }>(api(`/definitions/${enc(key)}?name=${enc(name)}`), flowJson, commandHeaders(operationId)),

    messages: (unreadOnly = false, signal?: AbortSignal) =>
      http.get<ApprovalMessage[]>(api(`/messages?unreadOnly=${unreadOnly}&take=30`), signal),
    unreadCount: (signal?: AbortSignal) => http.get<{ count: number }>(api('/messages/unread-count'), signal),
    markRead: (id: string, operationId: string) => http.post(api(`/messages/${id}/read`), {}, commandHeaders(operationId)),
    markAllRead: (operationId: string) => http.post(api('/messages/read-all'), {}, commandHeaders(operationId)),

    outbox: (query: ListQuery = {}, signal?: AbortSignal) =>
      http.get<PagedResult<OutboxMessage>>(api(`/outbox${queryString(query)}`), signal),
    outboxPayload: (id: string, signal?: AbortSignal) =>
      http.get<OutboxPayload>(api(`/outbox/${encodeURIComponent(id)}/payload`), signal),
    retryOutbox: (id: string, operationId: string) => http.post(api(`/outbox/${id}/retry`), {}, commandHeaders(operationId)),

    // ───── 管理（仅管理员）─────
    adminInstances: (query: ListQuery = {}, signal?: AbortSignal) =>
      http.get<PagedResult<InstanceListItem>>(api(`/admin/instances${queryString(query)}`), signal),
    terminate: (instanceId: string, reason: string, operationId: string) => http.post(api(`/admin/instances/${instanceId}/terminate`), { reason }, commandHeaders(operationId)),
    reassign: (taskId: string, toUserId: string, reason: string, operationId: string) =>
      http.post(api(`/admin/tasks/${taskId}/reassign`), { toUserId, reason }, commandHeaders(operationId)),
    /** 某人的未完成待办（离职交接预览） */
    userTasks: (userId: string, query: ListQuery = {}, signal?: AbortSignal) =>
      http.get<PagedResult<TodoItem>>(api(`/admin/users/${enc(userId)}/tasks${queryString(query)}`), signal),
    reassignUser: (userId: string, toUserId: string, reason: string, operationId: string) =>
      http.post<ReassignResult>(api(`/admin/users/${enc(userId)}/reassign`), { toUserId, reason }, commandHeaders(operationId)),
    statistics: (query: { from?: string; to?: string; definitionKey?: string }, signal?: AbortSignal) =>
      http.get<ApprovalStatistics>(api(`/admin/statistics${queryString(query)}`), signal),
  }
}

export type ApprovalApiClient = ReturnType<typeof createApprovalApi>
