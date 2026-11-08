import type { ApprovalHttpClient } from '@approval/shared'
import type { DemoUser, DemoWebhookState } from './types'
export function createDemoApi(http: ApprovalHttpClient) {
  return {
    users: () => http.get<DemoUser[]>('/api/demo/users'),
    webhook: (signal?: AbortSignal) => http.get<DemoWebhookState>('/api/demo/webhook', signal),
    webhookFailNext: (count: number) => http.post('/api/demo/webhook/fail-next?count=' + count, {}),
  }
}
