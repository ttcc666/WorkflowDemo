import { computed, nextTick, reactive, shallowRef, watch, type ComputedRef, type Ref, type WatchSource } from 'vue'
import { useRouter, type LocationQuery, type Router } from 'vue-router'
import type { ListQuery, PagedResult } from '@ui/contracts/api'
import { useAsyncData } from './useAsyncData'

/** 列表筛选条件（字符串形式，便于绑定筛选栏和同步到路由 query）。 */
export interface ListFilters {
  keyword: string
  status: string
  definitionKey: string
  /** 发起日期区间（YYYY-MM-DD，含两端） */
  from: string
  to: string
  businessType?: string
  businessKey?: string
  initiatorId?: string
  instanceId?: string
  approverId?: string
  sort?: string
  unreadOnly?: boolean
  overdueOnly: boolean
}

export const emptyFilters = (): ListFilters => ({ keyword: '', status: '', definitionKey: '', from: '', to: '', overdueOnly: false, businessType: '', businessKey: '', initiatorId: '', instanceId: '', approverId: '', sort: '', unreadOnly: false })

const filterKeys = Object.keys(emptyFilters()) as (keyof ListFilters)[]

/** 本地日期（YYYY-MM-DD）当天 0 点的 ISO 时间；addDays 用于区间结束日的次日 0 点（不含）。 */
export function localDayStart(date: string, addDays = 0) {
  const value = new Date(`${date}T00:00:00`)
  value.setDate(value.getDate() + addDays)
  return value.toISOString()
}

/** 筛选条件 → 接口参数：日期区间转为 [开始日 0 点, 结束日次日 0 点)。下拉清空后的值可能是 undefined，统一按空处理。 */
export function toListQuery(filters: ListFilters, page: number, pageSize: number): ListQuery {
  return {
    page,
    pageSize,
    keyword: (filters.keyword ?? '').trim() || undefined,
    status: filters.status || undefined,
    definitionKey: filters.definitionKey || undefined,
    from: filters.from ? localDayStart(filters.from) : undefined,
    to: filters.to ? localDayStart(filters.to, 1) : undefined,
    overdueOnly: filters.overdueOnly || undefined,
    businessType: filters.businessType?.trim() || undefined,
    businessKey: filters.businessKey?.trim() || undefined,
    initiatorId: filters.initiatorId?.trim() || undefined,
    instanceId: filters.instanceId?.trim() || undefined,
    approverId: filters.approverId?.trim() || undefined,
    sort: filters.sort || undefined,
    unreadOnly: filters.unreadOnly || undefined,
  }
}

/** 从路由 query 读取筛选与页码（键带前缀）。 */
export function readListState(query: LocationQuery, prefix: string, defaultPageSize: number) {
  const text = (key: string) => {
    const value = query[prefix + key]
    return typeof value === 'string' ? value : ''
  }
  const filters = emptyFilters()
  for (const key of filterKeys) {
    if (key === 'overdueOnly' || key === 'unreadOnly') filters[key] = text(key) === '1'
    else filters[key] = text(key)
  }
  const page = Math.max(1, Number.parseInt(text('page'), 10) || 1)
  const pageSize = Math.min(100, Math.max(1, Number.parseInt(text('pageSize'), 10) || defaultPageSize))
  return { filters, page, pageSize }
}

// 同一轮修改合并为一次 router.replace（同页多个列表同时变化时，避免互相覆盖）
const pendingPatches = new WeakMap<Router, Record<string, string | undefined>>()

function patchRouteQuery(router: Router, patch: Record<string, string | undefined>) {
  const pending = pendingPatches.get(router)
  if (pending) {
    Object.assign(pending, patch)
    return
  }
  const batch = { ...patch }
  pendingPatches.set(router, batch)
  void nextTick(() => {
    const merged: Record<string, string | null | (string | null)[]> = { ...router.currentRoute.value.query }
    for (const [key, value] of Object.entries(batch)) {
      if (value === undefined || value === '') delete merged[key]
      else merged[key] = value
    }
    pendingPatches.delete(router)
    void router.replace({ query: merged })
  })
}

export interface UsePagedListOptions {
  /** 同步到路由 query 的键前缀（同一页面有多个列表时区分，如 `done_`）；不传则不同步 */
  routePrefix?: string
  /** 这些值变化时回到第一页并重新加载（如当前用户） */
  resetOn?: WatchSource[]
  pageSize?: number
  /** 加载失败时是否弹出错误提示，默认 true */
  notifyError?: boolean
}

export interface PagedList<T> {
  items: ComputedRef<T[]>
  total: ComputedRef<number>
  loading: Readonly<Ref<boolean>>
  page: Ref<number>
  pageSize: Ref<number>
  /** 筛选栏编辑中的条件；调用 search() 后生效 */
  filters: ListFilters
  /** 应用筛选条件：回到第一页并重新加载 */
  search: () => void
  /** 清空筛选条件并重新加载 */
  reset: () => void
  reload: () => Promise<void>
}

/**
 * 分页列表：页码、每页条数、筛选条件（应用后才生效），可选同步到路由 query；
 * 页码或已应用的条件变化时自动加载，旧请求会被取消。
 */
export function usePagedList<T>(
  loader: (query: ListQuery, signal: AbortSignal) => Promise<PagedResult<T>>,
  options: UsePagedListOptions = {},
): PagedList<T> {
  const router = useRouter()
  const prefix = options.routePrefix
  const initial = readListState(prefix === undefined ? {} : router.currentRoute.value.query, prefix ?? '', options.pageSize ?? 20)

  const page = shallowRef(initial.page)
  const pageSize = shallowRef(initial.pageSize)
  const filters = reactive<ListFilters>({ ...initial.filters })
  const applied = shallowRef<ListFilters>({ ...initial.filters })

  const key = computed(() => JSON.stringify([applied.value, page.value, pageSize.value]))
  const data = useAsyncData((signal) => loader(toListQuery(applied.value, page.value, pageSize.value), signal), {
    watch: [key],
    notifyError: options.notifyError,
  })

  if (prefix !== undefined) {
    watch(key, () => {
      const patch: Record<string, string | undefined> = {
        [`${prefix}page`]: page.value > 1 ? String(page.value) : undefined,
        [`${prefix}pageSize`]: pageSize.value !== (options.pageSize ?? 20) ? String(pageSize.value) : undefined,
      }
      for (const name of filterKeys) {
        const value = applied.value[name]
        patch[prefix + name] = (name === 'overdueOnly' || name === 'unreadOnly') ? (value ? '1' : undefined) : (value as string) || undefined
      }
      patchRouteQuery(router, patch)
    })
  }

  if (options.resetOn?.length) {
    watch(options.resetOn, () => {
      data.data.value = undefined
      if (page.value !== 1) page.value = 1
      else void data.reload()
    })
  }

  function search() {
    const before = key.value
    applied.value = { ...filters }
    page.value = 1
    // 条件没变（如再次点击“查询”）时也刷新
    if (key.value === before) void data.reload()
  }

  function reset() {
    Object.assign(filters, emptyFilters())
    search()
  }

  return {
    items: computed(() => data.data.value?.items ?? []),
    total: computed(() => data.data.value?.total ?? 0),
    loading: data.loading,
    page,
    pageSize,
    filters,
    search,
    reset,
    reload: data.reload,
  }
}
