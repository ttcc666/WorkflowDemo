import { createRouter, createWebHistory } from 'vue-router'
import AppLayout from '@/layouts/AppLayout.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      component: AppLayout,
      children: [
        { path: '', redirect: '/workbench' },
        { path: 'workbench', name: 'workbench', component: () => import('@/views/WorkbenchView.vue') },
        { path: 'approvals/:id', name: 'approval-detail', component: () => import('@/views/ApprovalDetailView.vue') },
        { path: 'certificates', name: 'certificates', component: () => import('@/views/CertificatesView.vue') },
        { path: 'definitions', name: 'definitions', component: () => import('@/views/DefinitionsView.vue') },
        { path: 'outbox', name: 'outbox', component: () => import('@/views/OutboxView.vue') },
        // 设计器较重（Vue Flow），按需加载
        { path: 'designer/:key', name: 'designer', component: () => import('@/views/DesignerView.vue') },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/workbench' },
  ],
})
