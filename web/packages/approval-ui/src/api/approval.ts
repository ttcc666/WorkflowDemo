import { approvalConfig } from '../config'
import { http } from './http'
import type {
  AddSignMode,
  ApprovalDefinition,
  ApprovalInstance,
  ApprovalMe,
  ApprovalMessage,
  ApprovalMeta,
  ApprovalStatistics,
  ApprovalTask,
  DirectoryUser,
  FlowSummary,
  InstanceListItem,
  ListQuery,
  OutboxMessage,
  PagedResult,
  ReassignResult,
  ReturnTarget,
  StartForm,
  StartableFlow,
  TodoItem,
} from './types'
import type { FormValues } from '../features/forms/formModel'

const enc = encodeURIComponent
/** 接口地址，前缀取自配置的 apiBase */
const api = (path: string) => approvalConfig().apiBase + path

/** 查询字符串：忽略空值（undefined、null、''、false） */
export function queryString(query: object = {}) {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '' && value !== false) params.set(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

export const approvalApi = {
  me: (signal?: AbortSignal) => http.get<ApprovalMe>(api('/me'), signal),
  users: (keyword: string, take = 50, signal?: AbortSignal) =>
    http.get<DirectoryUser[]>(api(`/directory/users?keyword=${enc(keyword)}&take=${take}`), signal),
  usersByIds: (ids: string[], signal?: AbortSignal) =>
    http.get<DirectoryUser[]>(api(`/directory/users?ids=${ids.map(enc).join(',')}`), signal),
  roles: (signal?: AbortSignal) => http.get<string[]>(api('/directory/roles'), signal),

  startableForms: (signal?: AbortSignal) => http.get<StartableFlow[]>(api('/forms/startable'), signal),
  startForm: (key: string, signal?: AbortSignal) => http.get<StartForm>(api(`/forms/${enc(key)}`), signal),
  /** 发起表单审批；校验失败抛出 ApiError（errors 为字段错误） */
  submitForm: (key: string, formData: FormValues, comment: string) =>
    http.post<{ instanceId: string }>(api(`/forms/${enc(key)}/start`), { formData, comment }),

  todo: (query: ListQuery = {}, signal?: AbortSignal) => http.get<PagedResult<TodoItem>>(api(`/todo${queryString(query)}`), signal),
  todoCount: (signal?: AbortSignal) => http.get<{ count: number }>(api('/todo/count'), signal),
  done: (query: ListQuery = {}, signal?: AbortSignal) => http.get<PagedResult<InstanceListItem>>(api(`/done${queryString(query)}`), signal),
  started: (query: ListQuery = {}, signal?: AbortSignal) =>
    http.get<PagedResult<InstanceListItem>>(api(`/started${queryString(query)}`), signal),
  /** 抄送我的（含抄送给我所在角色的） */
  cc: (query: ListQuery = {}, signal?: AbortSignal) => http.get<PagedResult<InstanceListItem>>(api(`/cc${queryString(query)}`), signal),
  /** 已发布流程的标识与名称（筛选下拉） */
  flows: (signal?: AbortSignal) => http.get<FlowSummary[]>(api('/flows'), signal),

  instance: (id: string, signal?: AbortSignal) => http.get<ApprovalInstance>(api(`/instances/${id}`), signal),
  /** 实例绑定版本的流程定义（公开视图：不含动作参数、通知与时限配置），用于画流程图、展示表单 */
  instanceDefinition: (id: string, signal?: AbortSignal) => http.get<ApprovalDefinition>(api(`/instances/${id}/definition`), signal),
  byBusiness: (businessType: string, businessKey: string) =>
    http.get<ApprovalInstance[]>(api(`/instances?businessType=${enc(businessType)}&businessKey=${enc(businessKey)}`)),
  myTask: (instanceId: string, signal?: AbortSignal) =>
    http.get<{ task: ApprovalTask | null }>(api(`/instances/${instanceId}/my-task`), signal),

  approve: (taskId: string, comment: string) => http.post(api(`/tasks/${taskId}/approve`), { comment }),
  /** targetNodeId 为空时驳回并结束流程，否则退回到该节点 */
  reject: (taskId: string, comment: string, targetNodeId?: string) =>
    http.post(api(`/tasks/${taskId}/reject`), { comment, targetNodeId: targetNodeId ?? null }),
  returnTargets: (taskId: string, signal?: AbortSignal) => http.get<ReturnTarget[]>(api(`/tasks/${taskId}/return-targets`), signal),
  addSign: (taskId: string, userIds: string[], mode: AddSignMode, comment: string) =>
    http.post(api(`/tasks/${taskId}/add-sign`), { userIds, mode, comment }),
  /** formData 仅表单审批可传（修改后的表单） */
  resubmit: (taskId: string, comment: string, formData?: Record<string, unknown>) =>
    http.post(api(`/tasks/${taskId}/resubmit`), { comment, formData: formData ?? null }),
  transfer: (taskId: string, toUserId: string, comment: string) => http.post(api(`/tasks/${taskId}/transfer`), { toUserId, comment }),
  withdraw: (instanceId: string, comment: string) => http.post(api(`/instances/${instanceId}/withdraw`), { comment }),

  meta: () => http.get<ApprovalMeta>(api('/meta')),
  definitions: (signal?: AbortSignal) => http.get<ApprovalDefinition[]>(api('/definitions'), signal),
  latestDefinition: (key: string) => http.get<ApprovalDefinition>(api(`/definitions/${enc(key)}`)),
  definitionById: (id: string, signal?: AbortSignal) => http.get<ApprovalDefinition>(api(`/definitions/by-id/${id}`), signal),
  versions: (key: string, signal?: AbortSignal) => http.get<ApprovalDefinition[]>(api(`/definitions/${enc(key)}/versions`), signal),
  validate: (flowJson: string) => http.postJson<{ errors: string[] }>(api('/definitions/validate'), flowJson),
  publish: (key: string, name: string, flowJson: string) =>
    http.postJson<{ id: string; key: string; version: number }>(api(`/definitions/${enc(key)}?name=${enc(name)}`), flowJson),

  messages: (unreadOnly = false, signal?: AbortSignal) =>
    http.get<ApprovalMessage[]>(api(`/messages?unreadOnly=${unreadOnly}&take=30`), signal),
  unreadCount: (signal?: AbortSignal) => http.get<{ count: number }>(api('/messages/unread-count'), signal),
  markRead: (id: string) => http.post(api(`/messages/${id}/read`), {}),
  markAllRead: () => http.post(api('/messages/read-all'), {}),

  outbox: (query: ListQuery = {}, signal?: AbortSignal) =>
    http.get<PagedResult<OutboxMessage>>(api(`/outbox${queryString(query)}`), signal),
  retryOutbox: (id: string) => http.post(api(`/outbox/${id}/retry`), {}),

  // ───── 管理（仅管理员）─────
  adminInstances: (query: ListQuery = {}, signal?: AbortSignal) =>
    http.get<PagedResult<InstanceListItem>>(api(`/admin/instances${queryString(query)}`), signal),
  terminate: (instanceId: string, reason: string) => http.post(api(`/admin/instances/${instanceId}/terminate`), { reason }),
  reassign: (taskId: string, toUserId: string, reason: string) =>
    http.post(api(`/admin/tasks/${taskId}/reassign`), { toUserId, reason }),
  /** 某人的未完成待办（离职交接预览） */
  userTasks: (userId: string, query: ListQuery = {}, signal?: AbortSignal) =>
    http.get<PagedResult<TodoItem>>(api(`/admin/users/${enc(userId)}/tasks${queryString(query)}`), signal),
  reassignUser: (userId: string, toUserId: string, reason: string) =>
    http.post<ReassignResult>(api(`/admin/users/${enc(userId)}/reassign`), { toUserId, reason }),
  statistics: (query: { from?: string; to?: string; definitionKey?: string }, signal?: AbortSignal) =>
    http.get<ApprovalStatistics>(api(`/admin/statistics${queryString(query)}`), signal),
}
