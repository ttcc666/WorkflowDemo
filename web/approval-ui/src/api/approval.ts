import { http } from './http'
import type {
  ApprovalDefinition,
  ApprovalInstance,
  ApprovalMessage,
  ApprovalMeta,
  ApprovalTask,
  DemoUser,
  DemoWebhookState,
  OutboxMessage,
  OutboxStatus,
  TodoItem,
} from './types'

const base = '/api/approval'
const enc = encodeURIComponent

export const approvalApi = {
  todo: (signal?: AbortSignal) => http.get<TodoItem[]>(`${base}/todo`, signal),
  done: (signal?: AbortSignal) => http.get<ApprovalInstance[]>(`${base}/done`, signal),
  started: (signal?: AbortSignal) => http.get<ApprovalInstance[]>(`${base}/started`, signal),

  instance: (id: string, signal?: AbortSignal) => http.get<ApprovalInstance>(`${base}/instances/${id}`, signal),
  byBusiness: (businessType: string, businessKey: string) =>
    http.get<ApprovalInstance[]>(`${base}/instances?businessType=${enc(businessType)}&businessKey=${enc(businessKey)}`),
  myTask: (instanceId: string, signal?: AbortSignal) =>
    http.get<{ task: ApprovalTask | null }>(`${base}/instances/${instanceId}/my-task`, signal),

  approve: (taskId: string, comment: string) => http.post(`${base}/tasks/${taskId}/approve`, { comment }),
  reject: (taskId: string, comment: string) => http.post(`${base}/tasks/${taskId}/reject`, { comment }),
  transfer: (taskId: string, toUserId: string, comment: string) =>
    http.post(`${base}/tasks/${taskId}/transfer`, { toUserId, comment }),
  withdraw: (instanceId: string, comment: string) => http.post(`${base}/instances/${instanceId}/withdraw`, { comment }),

  meta: () => http.get<ApprovalMeta>(`${base}/meta`),
  definitions: (signal?: AbortSignal) => http.get<ApprovalDefinition[]>(`${base}/definitions`, signal),
  latestDefinition: (key: string) => http.get<ApprovalDefinition>(`${base}/definitions/${enc(key)}`),
  definitionById: (id: string, signal?: AbortSignal) => http.get<ApprovalDefinition>(`${base}/definitions/by-id/${id}`, signal),
  versions: (key: string, signal?: AbortSignal) => http.get<ApprovalDefinition[]>(`${base}/definitions/${enc(key)}/versions`, signal),
  validate: (flowJson: string) => http.postJson<{ errors: string[] }>(`${base}/definitions/validate`, flowJson),
  publish: (key: string, name: string, flowJson: string) =>
    http.postJson<{ id: string; key: string; version: number }>(`${base}/definitions/${enc(key)}?name=${enc(name)}`, flowJson),

  messages: (unreadOnly = false, signal?: AbortSignal) =>
    http.get<ApprovalMessage[]>(`${base}/messages?unreadOnly=${unreadOnly}&take=30`, signal),
  unreadCount: (signal?: AbortSignal) => http.get<{ count: number }>(`${base}/messages/unread-count`, signal),
  markRead: (id: string) => http.post(`${base}/messages/${id}/read`, {}),
  markAllRead: () => http.post(`${base}/messages/read-all`, {}),

  outbox: (status: OutboxStatus | '', signal?: AbortSignal) =>
    http.get<OutboxMessage[]>(`${base}/outbox${status ? `?status=${status}` : ''}`, signal),
  retryOutbox: (id: string) => http.post(`${base}/outbox/${id}/retry`, {}),
}

export const demoApi = {
  users: () => http.get<DemoUser[]>('/api/demo/users'),
  roles: () => http.get<string[]>('/api/demo/roles'),
  webhook: (signal?: AbortSignal) => http.get<DemoWebhookState>('/api/demo/webhook', signal),
  webhookFailNext: (count: number) => http.post(`/api/demo/webhook/fail-next?count=${count}`, {}),
}
