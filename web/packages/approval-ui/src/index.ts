import type { App, Plugin } from 'vue'
import { configureApprovalUI, type ApprovalUIOptions } from './config'
// 流程图（Vue Flow）样式；Element Plus 样式由宿主引入
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import './styles.css'

/**
 * 审批前端组件库插件。宿主需先安装 Element Plus 与 vue-router：
 * ```ts
 * app.use(ElementPlus).use(ApprovalUI, { headers: () => ({ Authorization: `Bearer ${token}` }) }).use(router)
 * ```
 */
export const ApprovalUI: Plugin<[ApprovalUIOptions?]> = {
  install(_app: App, options?: ApprovalUIOptions) {
    configureApprovalUI(options)
  },
}

export { approvalConfig, approvalUserKey, configureApprovalUI, type ApprovalUIOptions } from './config'
export { approvalRouteNames, createApprovalRoutes } from './routes'

export { approvalApi, queryString } from './api/approval'
export { ApiError, errorMessage, http } from './api/http'
export type * from './api/types'

export { useApprovalUser } from './composables/useApprovalUser'
export { useAsyncData } from './composables/useAsyncData'
export { useDirectory } from './composables/useDirectory'
export { emptyFilters, toListQuery, usePagedList, type ListFilters, type PagedList, type UsePagedListOptions } from './composables/usePagedList'

export { default as MessageBell } from './components/MessageBell.vue'
export { default as UserPicker } from './components/UserPicker.vue'
export { default as ApprovalTimeline } from './features/approval/ApprovalTimeline.vue'
export { default as FlowViewer } from './features/flow/FlowViewer.vue'
export { default as DynamicForm } from './features/forms/DynamicForm.vue'
export { default as FormDesigner } from './features/forms/FormDesigner.vue'
export {
  fieldRules,
  fieldTypes,
  fieldVariables,
  formOfDefinition,
  initialValues,
  parseFormValues,
  sumKey,
  validateValue,
} from './features/forms/formModel'

export * from './utils/labels'
