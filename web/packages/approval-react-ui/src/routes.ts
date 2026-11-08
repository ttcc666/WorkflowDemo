import type { RouteObject } from 'react-router'
export const approvalRouteNames = {
  workbench: 'approval-workbench',
  detail: 'approval-detail',
  start: 'approval-start',
  startForm: 'approval-start-form',
  definitions: 'approval-definitions',
  designer: 'approval-designer',
  monitor: 'approval-monitor',
  outbox: 'approval-outbox',
  diagnostics: 'approval-diagnostics',
} as const
export function createApprovalRoutes(): RouteObject[] {
  return [
    {
      path: 'workbench',
      lazy: () =>
        import('./pages/WorkbenchPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'approvals/:id',
      lazy: () =>
        import('./pages/ApprovalDetailPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'start',
      lazy: () =>
        import('./pages/StartCenterPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'start/:key',
      lazy: () =>
        import('./pages/StartFormPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'definitions',
      lazy: () =>
        import('./pages/DefinitionsPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'designer/:key',
      lazy: () =>
        import('./pages/DesignerPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'outbox',
      lazy: () =>
        import('./pages/OutboxPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'monitor',
      lazy: () =>
        import('./pages/MonitorPage').then((module) => ({
          Component: module.default,
        })),
    },
    {
      path: 'diagnostics',
      lazy: () =>
        import('./pages/DiagnosticsPage').then((module) => ({
          Component: module.default,
        })),
    },
  ]
}
