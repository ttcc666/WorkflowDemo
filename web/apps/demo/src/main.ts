import { createApp, defineAsyncComponent } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import { ApprovalUI } from '@approval/ui'
import './styles/main.css'
import App from './App.vue'
import { router } from './router'
import { DEMO_USER_HEADER, currentUserId } from './stores/session'

createApp(App)
  .use(ElementPlus)
  .use(ApprovalUI, {
    // 演示：当前用户经请求头传给后端（后端 DemoCurrentUser）；接入真实认证时改为 Authorization 头
    headers: () => ({ [DEMO_USER_HEADER]: currentUserId.value }),
    userKey: () => currentUserId.value,
    businessPanels: {
      Certificate: defineAsyncComponent(() => import('./features/certificates/CertificateRequestPanel.vue')),
    },
    outboxPanel: defineAsyncComponent(() => import('./components/DemoWebhookPanel.vue')),
  })
  .use(router)
  .mount('#app')
