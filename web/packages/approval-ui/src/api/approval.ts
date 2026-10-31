import { approvalConfig } from '../config'
import { http } from './http'
import type {
  AddSignMode,
  ApprovalDefinition,
  ApprovalInstance,
  ApprovalMe,
  ApprovalMessage,
  ApprovalMeta,
  ApprovalTask,
  DirectoryUser,
  InstanceListItem,
  OutboxMessage,
  OutboxStatus,
  ReturnTarget,
  StartForm,
  StartableFlow,
  TodoItem,
} from './types'
import type { FormValues } from '../features/forms/formModel'

const enc = encodeURIComponent
/** 接口地址，前缀取自配置的 apiBase */
const api = (path: string) => approvalConfig().apiBase + path

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

  todo: (signal?: AbortSignal) => http.get<TodoItem[]>(api('/todo'), signal),
  done: (signal?: AbortSignal) => http.get<InstanceListItem[]>(api('/done'), signal),
  started: (signal?: AbortSignal) => http.get<InstanceListItem[]>(api('/started'), signal),

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

  outbox: (status: OutboxStatus | '', signal?: AbortSignal) =>
    http.get<OutboxMessage[]>(api(`/outbox${status ? `?status=${status}` : ''}`), signal),
  retryOutbox: (id: string) => http.post(api(`/outbox/${id}/retry`), {}),
}
