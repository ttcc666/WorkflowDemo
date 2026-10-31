import { describe, expect, it } from 'vitest'
import { queryString } from '../api/approval'
import { emptyFilters, localDayStart, readListState, toListQuery } from './usePagedList'

describe('列表查询参数', () => {
  it('空条件不传，日期区间转为 [开始日 0 点, 结束日次日 0 点)', () => {
    const filters = { ...emptyFilters(), keyword: ' 报销 ', status: 'Running', from: '2026-10-01', to: '2026-10-31' }
    const query = toListQuery(filters, 2, 50)

    expect(query).toMatchObject({ page: 2, pageSize: 50, keyword: '报销', status: 'Running', definitionKey: undefined, overdueOnly: undefined })
    expect(query.from).toBe(new Date(2026, 9, 1).toISOString())
    expect(query.to).toBe(new Date(2026, 10, 1).toISOString())
    expect(localDayStart('2026-12-31', 1)).toBe(new Date(2027, 0, 1).toISOString())
  })

  it('下拉清空后的 undefined 按空处理', () => {
    const filters = { ...emptyFilters(), keyword: undefined as unknown as string, definitionKey: undefined as unknown as string }
    expect(toListQuery(filters, 1, 20)).toMatchObject({ keyword: undefined, definitionKey: undefined })
  })

  it('查询字符串忽略空值', () => {
    expect(queryString({ page: 1, keyword: '', status: undefined, overdueOnly: false, definitionKey: 'hr.leave' })).toBe('?page=1&definitionKey=hr.leave')
    expect(queryString({ overdueOnly: true, keyword: 'a b' })).toBe('?overdueOnly=true&keyword=a+b')
    expect(queryString({})).toBe('')
  })
})

describe('从路由读取列表状态', () => {
  it('按前缀读取筛选与页码，非法页码与超出上限的每页条数被修正', () => {
    const state = readListState({ done_page: '3', done_pageSize: '500', done_keyword: '证书', done_overdueOnly: '1', page: '9' }, 'done_', 20)

    expect(state.page).toBe(3)
    expect(state.pageSize).toBe(100)
    expect(state.filters).toEqual({ ...emptyFilters(), keyword: '证书', overdueOnly: true })
    expect(readListState({ done_page: 'x' }, 'done_', 20)).toMatchObject({ page: 1, pageSize: 20 })
  })
})
