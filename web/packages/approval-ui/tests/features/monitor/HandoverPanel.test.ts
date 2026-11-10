// @vitest-environment jsdom
import { webcrypto } from 'node:crypto'
import { afterEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createCommandIntent } from '@approval/shared'
import { createApprovalClient, getApprovalClientCore } from '@ui/client'
import HandoverPanel from '@ui/features/monitor/HandoverPanel.vue'

vi.mock('@ui/components/UserPicker.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ props: ['modelValue', 'placeholder', 'disabled'], emits: ['update:modelValue'],
    setup: (props, { emit }) => () => h('div', { 'data-picker': props.placeholder, 'data-selected': props.modelValue ?? '' }, ['alice', 'bob', 'carol'].map(id => h('button', { disabled: props.disabled, 'data-user': id, onClick: () => emit('update:modelValue', id) }, id))) }) }
})
let app: App | undefined
afterEach(() => { app?.unmount(); app = undefined; document.body.innerHTML = ''; sessionStorage.clear(); vi.restoreAllMocks(); vi.unstubAllGlobals() })
function show(allowed = true, fails = false) {
  vi.stubGlobal('crypto', webcrypto)
  const posts: { body: unknown; key: string | null }[] = []
  const reads: string[] = []
  const fetcher = vi.fn(async (url: string, init: RequestInit) => {
    if (init.method === 'POST') {
      posts.push({ body: JSON.parse(String(init.body)), key: new Headers(init.headers).get('Idempotency-Key') })
      if (fails) { fails = false; return Response.json({ title: '部分结果待核对' }, { status: 503 }) }
      return Response.json({ count: 1, skipped: [] })
    }
    reads.push(url)
    if (url.endsWith('/me')) return Response.json({ userId: 'admin', capabilities: { 'approval.task.reassign-bulk': allowed } })
    return Response.json({ items: [{ taskId: 't', instanceId: 'i', title: '实际待办', definitionName: '审批', nodeName: '审核', status: 'Pending', initiatorId: 'dave', createdAt: '' }], total: 1 })
  })
  const client = createApprovalClient({ fetcher })
  const container = document.createElement('div'); document.body.append(container)
  app = createApp(HandoverPanel).use(client).use(ElementPlus); app.mount(container)
  // Element Plus declares an intersection here although confirm resolves the action string at runtime.
  vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as Awaited<ReturnType<typeof ElMessageBox.confirm>>)
  const clickUser = async (picker: string, user: string) => { (container.querySelector(`[data-picker="${picker}"] [data-user="${user}"]`) as HTMLButtonElement).click(); await nextTick() }
  const submit = () => [...container.querySelectorAll('button')].find(button => button.textContent?.includes('确认改派'))!
  return { container, client, posts, reads, clickUser, submit }
}
it.each(['receiver', 'reason'])('Vue clears a completed handover when its %s changes without issuing another command', async field => {
  const view = show()
  await getApprovalClientCore(view.client).reloadUser(); await nextTick()
  await view.clickUser('选择离职或长期不在的人员', 'alice')
  await view.clickUser('选择接手人员', 'bob')
  await vi.waitFor(() => expect(view.submit().disabled).toBe(false))
  view.submit().click()
  await vi.waitFor(() => expect(view.container.querySelector('.handover__result')?.textContent).toContain('已改派 1 条'))
  expect(view.posts).toHaveLength(1)
  expect(view.posts[0]?.body).toEqual({ toUserId: 'bob', reason: '离职交接' })
  expect(view.posts[0]?.key).toBeTruthy()
  if (field === 'receiver') await view.clickUser('选择接手人员', 'carol')
  else { const input = view.container.querySelector('.handover__form input') as HTMLInputElement; input.value = '另一个业务意图'; input.dispatchEvent(new Event('input', { bubbles: true })); await nextTick() }
  expect(view.container.querySelector('.handover__result')).toBeNull()
  expect(view.posts).toHaveLength(1)
})
it('Vue discovers the old React namespace before sender selection and forbids replay without the original body', async () => {
  vi.stubGlobal('crypto', webcrypto)
  const legacy = createCommandIntent(() => ':/api/approval:user-reassign')
  await expect(legacy.run('alice', { from: 'alice', to: 'bob', reason: 'old' }, async () => { throw new TypeError('lost') })).rejects.toThrow('lost')
  const before = { ...sessionStorage }
  const view = show()
  await getApprovalClientCore(view.client).reloadUser(); await nextTick()
  expect(view.container.textContent).toContain('原交出人员：alice')
  expect(view.container.textContent).toContain('禁止自动重试')
  expect(view.container.textContent).not.toContain('按原内容重试上次操作')
  expect(view.submit().disabled).toBe(true)
  expect(view.posts).toHaveLength(0)
  expect({ ...sessionStorage }).toEqual(before)
})
it('Vue denies handover preview and submission when the host supplies no bulk permission', async () => {
  const view = show(false)
  await getApprovalClientCore(view.client).reloadUser(); await nextTick()
  expect(view.submit().disabled).toBe(true)
  expect(view.container.querySelector<HTMLButtonElement>('[data-user="alice"]')?.disabled).toBe(true)
  await view.clickUser('选择离职或长期不在的人员', 'alice')
  expect(view.reads.some(url => url.includes('/admin/users/'))).toBe(false)
  expect(view.posts).toHaveLength(0)
})
it('Vue recovery restores the original receiver and reason so the result describes the actual retried request', async () => {
  const view = show(true, true)
  await getApprovalClientCore(view.client).reloadUser(); await nextTick()
  await view.clickUser('选择离职或长期不在的人员', 'alice')
  await view.clickUser('选择接手人员', 'bob')
  await vi.waitFor(() => expect(view.submit().disabled).toBe(false))
  view.submit().click()
  await vi.waitFor(() => expect(view.container.textContent).toContain('上次提交结果尚未确认'))
  await vi.waitFor(() => expect(view.container.querySelector<HTMLButtonElement>('[data-picker="选择接手人员"] [data-user="carol"]')?.disabled).toBe(false))
  await view.clickUser('选择接手人员', 'carol')
  expect(view.container.querySelector('[data-picker="选择接手人员"]')?.getAttribute('data-selected')).toBe('carol')
  const input = view.container.querySelector('.handover__form input') as HTMLInputElement
  input.value = '修改后的原因'; input.dispatchEvent(new Event('input', { bubbles: true })); await nextTick()
  const retry = [...view.container.querySelectorAll('button')].find(button => button.textContent?.includes('按原内容重试上次操作'))!
  retry.click()
  await vi.waitFor(() => expect(view.container.querySelector('.handover__result')?.textContent).toContain('已改派 1 条'))
  expect(view.posts).toHaveLength(2)
  expect(view.posts[1]).toEqual(view.posts[0])
  expect(input.value).toBe('离职交接')
  expect(view.container.querySelector('[data-picker="选择接手人员"]')?.getAttribute('data-selected')).toBe('bob')
})
