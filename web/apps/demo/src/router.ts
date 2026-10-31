import { createRouter, createWebHistory } from 'vue-router'
import { createApprovalRoutes } from '@approval/ui'
import AppLayout from '@/layouts/AppLayout.vue'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      component: AppLayout,
      children: [
        { path: '', redirect: '/workbench' },
        // 审批工作台、详情、流程定义、设计器、投递记录
        ...createApprovalRoutes(),
        { path: 'certificates', name: 'certificates', component: () => import('@/views/CertificatesView.vue') },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/workbench' },
  ],
})
