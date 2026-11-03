import { computed, effectScope, watch, hasInjectionContext, inject, type App, type ComputedRef, type InjectionKey } from 'vue'
import { createApprovalApi, type ApprovalApiClient } from './api/approval'
import { createHttpClient, type ApprovalHttpClient } from './api/http'
import { approvalIdentityKey, resolveApprovalUIOptions, type ApprovalUIOptions, type ResolvedApprovalUIOptions } from './config'

export interface ApprovalClient {
  readonly api: ApprovalApiClient
  readonly http: ApprovalHttpClient
  readonly userKey: ComputedRef<string>
  config: () => ResolvedApprovalUIOptions
  /** 可直接 app.use(client)，让组件与 composables 使用此实例。 */
  install: (app: App) => void
  /** 释放此实例的共享缓存、监听与缓存加载请求。 */
  dispose: () => void
}

export const approvalClientKey: InjectionKey<ApprovalClient> = Symbol('approval-client')
const resources = new WeakMap<ApprovalClient, Set<() => void>>()

/** 包内共享状态在客户端释放时清理；不绑定到第一个使用它的组件。 */
export function registerClientCleanup(client: ApprovalClient, cleanup: () => void) {
  let callbacks = resources.get(client)
  if (!callbacks) resources.set(client, callbacks = new Set())
  callbacks.add(cleanup)
}

function managedClient(
  config: () => ResolvedApprovalUIOptions,
  getHttp: () => ApprovalHttpClient,
  getApi: () => ApprovalApiClient,
): ApprovalClient {
  let installations = 0
  const scope = effectScope(true)
  const client: ApprovalClient = {
    config,
    userKey: computed(() => approvalIdentityKey(config())),
    get http() { return getHttp() },
    get api() { return getApi() },
    install(app) {
      app.provide(approvalClientKey, client)
      installations++
      app.onUnmount(() => {
        if (--installations === 0) client.dispose()
      })
    },
    dispose() {
      scope.stop()
      getHttp().abortPending()
      const callbacks = resources.get(client)
      resources.delete(client)
      for (const cleanup of callbacks ?? []) cleanup()
    },
  }
  scope.run(() => watch(client.userKey, () => getHttp().abortPending(), { flush: 'sync' }))
  return client
}

/** 独立客户端，也可作为 Vue 插件安装；每个实例持有自己的配置与资源。 */
export function createApprovalClient(options: ApprovalUIOptions = {}): ApprovalClient {
  const resolved = resolveApprovalUIOptions(options)
  const config = () => resolved
  const transport = createHttpClient(config)
  const api = createApprovalApi(transport, config)
  return managedClient(config, () => transport, () => api)
}

/** 在 setup 中获取客户端并保留引用，事件和异步回调继续使用同一实例。 */
export function useApprovalClient(): ApprovalClient {
  const client = hasInjectionContext() ? inject(approvalClientKey, null) : null
  if (!client) throw new Error('请先通过 app.use(createApprovalClient(options)) 或 app.use(ApprovalUI, options) 安装审批客户端')
  return client
}
