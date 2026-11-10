// @vitest-environment jsdom
import { webcrypto } from 'node:crypto'
import { afterEach, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, shallowRef, type App } from 'vue'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createCommandIntent as coreIntent } from '@approval/shared'
import { createApprovalClient } from '@ui/client'
import { useCommandIntent } from '@ui/composables/useCommandIntent'
import CommandRecovery from '@ui/components/CommandRecovery.vue'

let app: App | undefined
afterEach(() => { app?.unmount(); app = undefined; document.body.innerHTML = ''; sessionStorage.clear(); vi.restoreAllMocks(); vi.unstubAllGlobals() })
const owner = 'alice:/api/approval:recovery-p2'
async function seed(scope: string) {
  vi.stubGlobal('crypto', webcrypto)
  await expect(coreIntent(() => owner).run(scope, { secret: 'private body' }, async () => { throw new Error('lost') })).rejects.toThrow('lost')
}
function show(initial?: string | null, canExecute?: (scope: string) => boolean) {
  vi.stubGlobal('crypto', webcrypto)
  const selected = shallowRef(initial), actor = shallowRef('alice')
  let intent!: ReturnType<typeof useCommandIntent>
  const recovered = vi.fn(), reset = vi.fn()
  const client = createApprovalClient({ userKey: () => actor.value, fetcher: async () => Response.json({}) })
  const view = defineComponent({ setup() { intent = useCommandIntent('recovery-p2', { canExecute }); return () => h(CommandRecovery, { intent, scope: selected.value, onRecovered: recovered, onReset: reset }) } })
  const container = document.createElement('div'); document.body.append(container)
  app = createApp(view).use(client).use(ElementPlus); app.mount(container)
  return { container, selected, actor, recovered, reset, get intent() { return intent } }
}
function row(container: HTMLElement, scope: string) { return [...container.querySelectorAll('.el-alert')].find(item => [...item.querySelectorAll('p')].some(node => node.textContent?.replace(/\s/g, '') === '操作：' + scope))! }
function button(element: Element, text: string) { return [...element.querySelectorAll('button')].find(item => item.textContent?.includes(text))! }
it('Vue discovers every refreshed scope without retrying and confirms only one reset', async () => {
  await seed('draft:a'); await seed('start:a')
  const mint = vi.spyOn(crypto, 'randomUUID')
  const view = show()
  expect(view.container.querySelectorAll('.el-alert')).toHaveLength(2)
  expect(view.container.textContent).toContain('当前页面已无原请求正文')
  expect(view.container.textContent).not.toContain('按原内容重试上次操作')
  let approve!: (value: Awaited<ReturnType<typeof ElMessageBox.confirm>>) => void
  vi.spyOn(ElMessageBox, 'confirm').mockImplementation(() => new Promise(resolve => { approve = resolve }))
  button(row(view.container, 'draft:a'), '核对结果后开始新操作').click(); await nextTick()
  expect(view.intent.pendingScopes()).toEqual(['draft:a', 'start:a'])
  approve('confirm' as Awaited<ReturnType<typeof ElMessageBox.confirm>>)
  await vi.waitFor(() => expect(view.intent.pendingScopes()).toEqual(['start:a']))
  await nextTick()
  expect(view.container.textContent).not.toContain('操作：draft:a')
  expect(view.container.textContent).toContain('操作：start:a')
  expect(view.reset).toHaveBeenCalledWith('draft:a')
  expect(mint).not.toHaveBeenCalled()
})
it('Vue reacts to peer changes and preserves explicit scope null and identity isolation', async () => {
  await seed('draft:a'); await seed('start:a')
  const view = show(null)
  expect(view.container.textContent).not.toContain('上次提交结果尚未确认')
  view.selected.value = 'start:a'; await nextTick()
  expect(view.container.querySelectorAll('.el-alert')).toHaveLength(1)
  view.selected.value = undefined; await nextTick()
  await seed('submit:b'); await nextTick()
  expect(view.container.querySelectorAll('.el-alert')).toHaveLength(3)
  coreIntent(() => owner).reset('start:a'); await nextTick()
  expect(view.container.textContent).not.toContain('操作：start:a')
  view.actor.value = 'bob'; await nextTick()
  expect(view.container.textContent).not.toContain('上次提交结果尚未确认')
  view.actor.value = 'alice'; await nextTick()
  expect(view.container.querySelectorAll('.el-alert')).toHaveLength(2)
})
it('Vue retries the live original body and key and keeps other records visible', async () => {
  await seed('draft:a')
  const view = show(), send = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockResolvedValueOnce({ result: 'confirmed' })
  await expect(view.intent.run('submit:b', { secret: 'original' }, send)).rejects.toThrow('lost'); await nextTick()
  const retry = button(row(view.container, 'submit:b'), '按原内容重试上次操作')
  retry.click(); retry.click()
  await vi.waitFor(() => expect(view.recovered).toHaveBeenCalledWith({ result: 'confirmed' }, 'submit:b'))
  expect(send.mock.calls[1]).toEqual(send.mock.calls[0])
  expect(send).toHaveBeenCalledTimes(2)
  await nextTick()
  expect(view.container.textContent).not.toContain('操作：submit:b')
  expect(view.container.textContent).toContain('操作：draft:a')
  expect(view.intent.hasPending('draft:a')).toBe(true)
})
it('Vue keeps concurrent recovery rows independent until both original operations settle', async () => {
  const view = show()
  const releases = new Map<string, () => void>()
  for (const scope of ['draft:a', 'start:b']) {
    const send = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockImplementationOnce(() => new Promise<void>(resolve => { releases.set(scope, resolve) }))
    await expect(view.intent.run(scope, { scope }, send)).rejects.toThrow('lost'); await nextTick()
    button(row(view.container, scope), '按原内容重试上次操作').click()
    await vi.waitFor(() => expect(send).toHaveBeenCalledTimes(2))
    expect(send.mock.calls[1]).toEqual(send.mock.calls[0])
  }
  releases.get('draft:a')!()
  await vi.waitFor(() => expect(view.recovered.mock.calls).toEqual([[undefined, 'draft:a']]))
  await nextTick()
  expect(view.container.textContent).not.toContain('操作：draft:a')
  expect(row(view.container, 'start:b').textContent).toContain('正在确认原操作结果')
  releases.get('start:b')!()
  await vi.waitFor(() => expect(view.recovered.mock.calls).toEqual([[undefined, 'draft:a'], [undefined, 'start:b']]))
  await nextTick()
  expect(view.container.querySelectorAll('.el-alert')).toHaveLength(0)
  expect(view.intent.pendingScopes()).toEqual([])
})

it('Vue checks the current scope before replay and preserves a different flow original intent', async () => {
  let allowed = 'draft:a'
  const view = show(undefined, scope => scope === allowed)
  const send = vi.fn().mockRejectedValueOnce(new TypeError('lost')).mockResolvedValueOnce({ draftId: 'original' })
  await expect(view.intent.run('draft:a', { original: 'body' }, send)).rejects.toThrow('lost')
  await nextTick()
  allowed = 'start:b'
  button(row(view.container, 'draft:a'), '按原内容重试上次操作').click()
  await vi.waitFor(() => expect(view.container.textContent).toContain('没有执行此操作的权限'))
  expect(send).toHaveBeenCalledTimes(1)
  expect(view.intent.hasPending('draft:a')).toBe(true)
  expect(view.recovered).not.toHaveBeenCalled()
})
