import { computed, shallowRef, type Component } from 'vue'

/** @approval/ui 的配置，由 `app.use(ApprovalUI, options)` 传入。 */
export interface ApprovalUIOptions {
  /** 审批接口前缀，与后端 `MapApprovalEndpoints(prefix)` 一致，默认 `/api/approval` */
  apiBase?: string
  /** 每个请求附加的请求头（如 `Authorization`）；每次请求时调用，可返回最新令牌 */
  headers?: () => Record<string, string>
  /** 宿主认证请求桥接：保留原始 Response，支持令牌刷新、取消和二进制响应。 */
  fetcher?: (url: string, init: RequestInit) => Promise<Response>
  /** 接口返回 401 时调用（如跳转登录页） */
  onUnauthorized?: () => void
  /**
   * 当前用户标识（响应式 getter）。值变化时工作台、详情、站内信等自动重新加载。
   * 真实认证下用户切换通常伴随页面刷新，可不配置；演示环境切换用户时需要。
   */
  userKey?: () => string | undefined
  /** 当前企业标识（响应式 getter）；切换企业时隔离缓存并取消旧请求。认证及成员资格由宿主服务端验证。 */
  tenantId?: () => string | undefined
  /**
   * 业务详情面板：审批详情页按 BusinessType 显示对应组件（props：`businessKey`）。
   * 内置 `Form`（表单审批）；未注册的业务显示原始表单数据。
   */
  businessPanels?: Record<string, Component>
  /** 投递记录页底部的附加面板（如演示用的模拟外部系统） */
  outboxPanel?: Component
}

export interface ResolvedApprovalUIOptions extends ApprovalUIOptions {
  apiBase: string
  businessPanels: Record<string, Component>
}

const current = shallowRef<ResolvedApprovalUIOptions>({ apiBase: '/api/approval', businessPanels: {} })

/** 为一个客户端解析配置；业务面板映射按实例复制。 */
export function resolveApprovalUIOptions(options: ApprovalUIOptions = {}): ResolvedApprovalUIOptions {
  return {
    ...options,
    apiBase: (options.apiBase ?? '/api/approval').replace(/\/+$/, ''),
    businessPanels: { ...options.businessPanels },
  }
}

/** 兼容单实例配置。隔离客户端使用 createApprovalClient(options)。 */
export function configureApprovalUI(options: ApprovalUIOptions = {}) {
  current.value = resolveApprovalUIOptions(options)
}

export function approvalConfig(): ResolvedApprovalUIOptions {
  return current.value
}

/** 当前用户标识的响应式来源，供各页面在切换用户时刷新数据。 */
export function approvalIdentityKey(options: Pick<ApprovalUIOptions, 'tenantId' | 'userKey'>) {
  return options.tenantId ? JSON.stringify([options.tenantId() ?? '', options.userKey?.() ?? '']) : options.userKey?.() ?? ''
}
export const approvalUserKey = computed(() => approvalIdentityKey(current.value))
