import { defineAsyncComponent } from 'vue'
import { createApprovalClient } from '@approval/ui'
import { DEMO_USER_HEADER, currentUserId } from './stores/session'

/** 示例宿主拥有自己的客户端；组件内注入，组件外显式复用。 */
export const approval = createApprovalClient({
  headers: () => ({ [DEMO_USER_HEADER]: currentUserId.value }),
  userKey: () => currentUserId.value,
  businessPanels: {
    Certificate: defineAsyncComponent(() => import('./features/certificates/CertificateRequestPanel.vue')),
  },
  outboxPanel: defineAsyncComponent(() => import('./components/DemoWebhookPanel.vue')),
})
