import { computed, shallowRef, type Component } from 'vue'

/** @approval/ui 的配置，由 `app.use(ApprovalUI, options)` 传入。 */
export interface ApprovalUIOptions {
  /** 审批接口前缀，与后端 `MapApprovalEndpoints(prefix)` 一致，默认 `/api/approval` */
  apiBase?: string
  /** 每个请求附加的请求头（如 `Authorization`）；每次请求时调用，可返回最新令牌 */
  headers?: () => Record<string, string>
  /** 接口返回 401 时调用（如跳转登录页） */
  onUnauthorized?: () => void
  /**
   * 当前用户标识（响应式 getter）。值变化时工作台、详情、站内信等自动重新加载。
   * 真实认证下用户切换通常伴随页面刷新，可不配置；演示环境切换用户时需要。
   */
  userKey?: () => string | undefined
  /**
   * 业务详情面板：审批详情页按 BusinessType 显示对应组件（props：`businessKey`）。
   * 内置 `Form`（表单审批）；未注册的业务显示原始表单数据。
   */
  businessPanels?: Record<string, Component>
  /** 投递记录页底部的附加面板（如演示用的模拟外部系统） */
  outboxPanel?: Component
}

interface ResolvedOptions extends ApprovalUIOptions {
  apiBase: string
  businessPanels: Record<string, Component>
}

const current = shallowRef<ResolvedOptions>({ apiBase: '/api/approval', businessPanels: {} })

/** 设置全局配置（插件安装时调用）；非组件代码（如 http）也从这里读取，因此不使用 provide / inject。 */
export function configureApprovalUI(options: ApprovalUIOptions = {}) {
  current.value = {
    ...options,
    apiBase: (options.apiBase ?? '/api/approval').replace(/\/+$/, ''),
    businessPanels: { ...options.businessPanels },
  }
}

export function approvalConfig(): ResolvedOptions {
  return current.value
}

/** 当前用户标识的响应式来源，供各页面在切换用户时刷新数据。 */
export const approvalUserKey = computed(() => current.value.userKey?.() ?? '')
