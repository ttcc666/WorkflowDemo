import { approval } from '../approval'
import type { DemoUser, DemoWebhookState } from './types'

const http = approval.http

/** 仅演示：用户切换器、模拟外部系统（Webhook 接收端）。 */
export const demoApi = {
  users: () => http.get<DemoUser[]>('/api/demo/users'),
  webhook: (signal?: AbortSignal) => http.get<DemoWebhookState>('/api/demo/webhook', signal),
  webhookFailNext: (count: number) => http.post(`/api/demo/webhook/fail-next?count=${count}`, {}),
}
