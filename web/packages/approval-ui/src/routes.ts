import type { RouteRecordRaw } from 'vue-router'

/** 路由名称（统一前缀 approval-，避免与宿主冲突）。 */
export const approvalRouteNames = {
  workbench: 'approval-workbench',
  detail: 'approval-detail',
  definitions: 'approval-definitions',
  designer: 'approval-designer',
  outbox: 'approval-outbox',
  start: 'approval-start',
  startForm: 'approval-start-form',
} as const

/**
 * 审批页面路由，挂载到宿主布局的 children 中：
 * `workbench`、`approvals/:id`、`start`、`start/:key`、`definitions`、`designer/:key`、`outbox`。
 * 页面按需加载（设计器依赖 Vue Flow，体积较大）。
 */
export function createApprovalRoutes(): RouteRecordRaw[] {
  return [
    { path: 'workbench', name: approvalRouteNames.workbench, component: () => import('./views/WorkbenchView.vue') },
    { path: 'approvals/:id', name: approvalRouteNames.detail, component: () => import('./views/ApprovalDetailView.vue') },
    { path: 'start', name: approvalRouteNames.start, component: () => import('./views/StartCenterView.vue') },
    { path: 'start/:key', name: approvalRouteNames.startForm, component: () => import('./views/StartFormView.vue') },
    { path: 'definitions', name: approvalRouteNames.definitions, component: () => import('./views/DefinitionsView.vue') },
    { path: 'designer/:key', name: approvalRouteNames.designer, component: () => import('./views/DesignerView.vue') },
    { path: 'outbox', name: approvalRouteNames.outbox, component: () => import('./views/OutboxView.vue') },
  ]
}
