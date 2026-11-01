import { afterEach, expect, it, vi } from 'vitest'
import { createApp, effectScope, type EffectScope } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { usePagedList } from './usePagedList'

const scopes: EffectScope[] = []
afterEach(() => scopes.splice(0).forEach(scope => scope.stop()))

async function router(keep: string) {
  const value = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { render: () => null } }] })
  await value.push({ path: '/', query: { keep } })
  return value
}

function list(router: Router, prefix: string) {
  const app = createApp({}).use(router)
  const scope = effectScope()
  scopes.push(scope)
  return scope.run(() => app.runWithContext(() => usePagedList(async () => ({ items: [], total: 0, page: 1, pageSize: 20 }), { routePrefix: prefix })))!
}

it('keeps same-tick query patches isolated between routers', async () => {
  const [left, right] = await Promise.all([router('left'), router('right')])
  const leftList = list(left, 'list_')
  const rightList = list(right, 'list_')
  leftList.filters.keyword = 'left-filter'
  rightList.filters.keyword = 'right-filter'
  leftList.search()
  rightList.search()
  await vi.waitFor(() => {
    expect(left.currentRoute.value.query).toEqual({ keep: 'left', list_keyword: 'left-filter' })
    expect(right.currentRoute.value.query).toEqual({ keep: 'right', list_keyword: 'right-filter' })
  })
})

it('still merges same-tick query patches for lists sharing one router', async () => {
  const shared = await router('original')
  const first = list(shared, 'first_')
  const second = list(shared, 'second_')
  first.filters.keyword = 'one'
  second.filters.keyword = 'two'
  first.search()
  second.search()
  await vi.waitFor(() => expect(shared.currentRoute.value.query).toEqual({ keep: 'original', first_keyword: 'one', second_keyword: 'two' }))
})
