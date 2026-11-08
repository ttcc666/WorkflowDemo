import type { ListQuery } from '../../contracts/api.js'
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

export const listFilterKeys = Object.keys(emptyFilters()) as (keyof ListFilters)[]

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
export function readListState(query: Record<string, string | null | (string | null)[] | undefined>, prefix: string, defaultPageSize: number) {
  const text = (key: string) => {
    const value = query[prefix + key]
    return typeof value === 'string' ? value : ''
  }
  const filters = emptyFilters()
  for (const key of listFilterKeys) {
    if (key === 'overdueOnly' || key === 'unreadOnly') filters[key] = text(key) === '1'
    else filters[key] = text(key)
  }
  const page = Math.max(1, Number.parseInt(text('page'), 10) || 1)
  const pageSize = Math.min(100, Math.max(1, Number.parseInt(text('pageSize'), 10) || defaultPageSize))
  return { filters, page, pageSize }
}
