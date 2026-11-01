import { readonly, shallowRef, watch, type Ref, type ShallowRef, type WatchSource } from 'vue'
import { ElMessage } from 'element-plus'
import { errorMessage } from '../api/http'

interface UseAsyncDataOptions<T> {
  /** 变化时自动重新加载（如路由参数、当前用户） */
  watch?: WatchSource[]
  initial?: T
  /** 加载失败时是否弹出错误提示，默认 true */
  notifyError?: boolean
  /** 身份或资源变化时清空旧数据，避免失败响应之后继续展示前一份敏感内容 */
  resetOnReload?: boolean
}

export interface AsyncData<T> {
  data: ShallowRef<T | undefined>
  loading: Readonly<Ref<boolean>>
  error: Readonly<Ref<string | null>>
  reload: () => Promise<void>
}

/**
 * 通用异步加载：loading / error / reload，并在重新加载时取消上一次未完成的请求，避免旧响应覆盖新数据。
 */
export function useAsyncData<T>(loader: (signal: AbortSignal) => Promise<T>, options: UseAsyncDataOptions<T> = {}): AsyncData<T> {
  const data = shallowRef<T | undefined>(options.initial)
  const loading = shallowRef(false)
  const error = shallowRef<string | null>(null)
  let controller: AbortController | null = null

  async function reload() {
    controller?.abort()
    if (options.resetOnReload) data.value = options.initial
    const current = (controller = new AbortController())
    loading.value = true
    error.value = null
    try {
      const result = await loader(current.signal)
      if (current === controller) data.value = result
    } catch (e) {
      if (current.signal.aborted) return
      error.value = errorMessage(e)
      if (options.notifyError !== false) ElMessage.error(error.value)
    } finally {
      if (current === controller) loading.value = false
    }
  }

  watch(options.watch ?? [], reload, { immediate: true })

  return { data, loading: readonly(loading), error: readonly(error), reload }
}
