import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { chooseOption } from './ui-controls'

type Framework = 'React' | 'Vue'
type Operation = 'start' | 'draft'
const api = () => process.env.APPROVAL_SMOKE_API! + '/api/approval'
const headers = (user = 'alice') => ({ 'X-User-Id': user, 'Idempotency-Key': crypto.randomUUID() })
async function define(request: APIRequestContext) {
  const key = 'form-recovery-' + crypto.randomUUID()
  const response = await request.post(`${api()}/definitions/${key}?name=${key}`, { headers: headers('admin'), data: {
    schemaVersion: 1, startNodeId: 'start', settings: { startable: true },
    form: { fields: [{ key: 'subject', label: '恢复主题', type: 'Text', required: true, defaultValue: 'Original' }] },
    nodes: { start: { id: 'start', name: '开始', type: 'Start', next: 'review' },
      review: { id: 'review', name: '审批', type: 'Approval', approvers: [{ type: 'User', value: 'dave' }], next: 'end' },
      end: { id: 'end', name: '结束', type: 'End' } },
  } })
  expect(response.ok(), await response.text()).toBe(true)
  return key
}
async function open(page: Page, framework: Framework, key: string) {
  await page.addInitScript(() => { if (!localStorage.getItem('approval-demo-user')) localStorage.setItem('approval-demo-user', 'alice') })
  await page.goto((framework === 'Vue' ? process.env.APPROVAL_VUE_URL! : '') + `/start/${key}?source=workbench&tab=mine`)
  await expect(page.getByRole('textbox', { name: /^\*?恢复主题$/ })).toHaveValue('Original')
}
async function send(page: Page, operation: Operation) {
  await page.getByRole('button', { name: operation === 'start' ? '提交申请' : '保存草稿', exact: true }).click()
  await expect(page.getByRole('button', { name: '按原内容重试上次操作', exact: true })).toBeVisible()
}
async function records(request: APIRequestContext, key: string, operation: Operation) {
  const response = await request.get(api() + (operation === 'start' ? `/started?definitionKey=${key}&pageSize=100` : '/forms/drafts'), { headers: headers() })
  expect(response.ok()).toBe(true)
  const value = await response.json()
  const items: { id: string; definitionKey: string }[] = operation === 'start' ? value.items : value
  return items.filter(item => item.definitionKey === key)
}
async function pending(page: Page, key: string, operation: Operation) {
  return page.evaluate(({ key, operation }) => sessionStorage.getItem(`approval.intent.v1:${encodeURIComponent('alice:/api/approval:start-form')}:${encodeURIComponent(`${operation}:${key}`)}`), { key, operation })
}

for (const framework of ['React', 'Vue'] as const) {
  for (const operation of ['start', 'draft'] as const) {
    for (const refresh of [false, true]) {
      test(`${framework} recovers committed ${operation} ${refresh ? 'after refresh and restoring the original body' : 'in the current page'} without duplicate records`, async ({ page, request }) => {
        const key = await define(request)
        const sent: { key?: string; body: string | null }[] = [], responses: { instanceId?: string; draftId?: string; updatedAt?: string }[] = []
        let losses = refresh ? 2 : 1
        await page.route(`**/api/approval/forms/${key}/${operation}`, async route => {
          sent.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() })
          const response = await route.fetch()
          expect(response.ok(), await response.text()).toBe(true)
          responses.push(await response.json())
          if (losses-- > 0) await route.abort('failed'); else await route.fulfill({ response })
        })
        await open(page, framework, key)
        await send(page, operation)
        if (refresh) {
          await page.reload()
          await expect(page.getByRole('textbox', { name: /^\*?恢复主题$/ })).toHaveValue('Original')
          await expect(page.getByRole('button', { name: '按原内容重试上次操作', exact: true })).toHaveCount(0)
          expect(await pending(page, key, operation)).not.toBeNull()
          // Sensitive bodies are not stored. The same form content must be restored before replay.
          await send(page, operation)
        }
        await page.getByRole('button', { name: '按原内容重试上次操作', exact: true }).click()
        const original = responses[0]!
        if (operation === 'start') {
          await expect(page).toHaveURL(new RegExp(`/approvals/${original.instanceId}$`))
        } else {
          await expect.poll(() => new URL(page.url()).searchParams.get('draft')).toBe(original.draftId)
          expect(new URL(page.url()).searchParams.get('source')).toBe('workbench')
          expect(new URL(page.url()).searchParams.get('tab')).toBe('mine')
          const state = page.locator(framework === 'Vue' ? '.start-form__saved' : '.ar-save-state')
          await expect(state).toContainText(framework === 'Vue' ? '草稿已保存' : '最后保存')
          await expect(state).not.toContainText(framework === 'Vue' ? '有新的修改' : '有未保存的修改')
          const draft = await request.get(`${api()}/forms/drafts/${original.draftId}`, { headers: headers() })
          expect(draft.ok()).toBe(true)
          const persisted = await draft.json()
          expect(persisted.updatedAt).toBe(original.updatedAt)
          expect(JSON.parse(persisted.formDataJson).subject).toBe('Original')
          await page.getByRole('textbox', { name: /^\*?恢复主题$/ }).fill('Unsaved edit')
          await expect(state).toContainText(framework === 'Vue' ? '有新的修改' : '有未保存的修改')
        }
        expect(sent).toHaveLength(refresh ? 3 : 2)
        expect(sent[0]!.key).toBeTruthy()
        for (const post of sent) expect(post).toEqual(sent[0])
        for (const response of responses) expect(response).toEqual(original)
        expect(await records(request, key, operation)).toEqual([expect.objectContaining({ id: operation === 'start' ? original.instanceId : original.draftId })])
        expect(await pending(page, key, operation)).toBeNull()
        if (operation === 'draft') {
          const saved = page.waitForResponse(response => response.url().endsWith(`/forms/${key}/draft`) && response.request().method() === 'PUT')
          await page.getByRole('button', { name: '保存草稿', exact: true }).click()
          const updated = await saved
          expect(updated.ok()).toBe(true)
          expect((await updated.json()).draftId).toBe(original.draftId)
          expect(JSON.parse(sent.at(-1)!.body!).draftId).toBe(original.draftId)
          expect(sent.at(-1)!.key).not.toBe(sent[0]!.key)
          expect(await records(request, key, operation)).toEqual([expect.objectContaining({ id: original.draftId })])
          const persisted = await request.get(`${api()}/forms/drafts/${original.draftId}`, { headers: headers() })
          expect(JSON.parse((await persisted.json()).formDataJson).subject).toBe('Unsaved edit')
        }
      })
    }
    for (const change of ['identity', 'flow', 'leave'] as const) {
      test(`${framework} ignores committed ${operation} recovery after ${change} changes`, async ({ page, request }) => {
        const key = await define(request), other = await define(request)
        let calls = 0, release!: () => void, arrived!: () => void, finished!: () => void
        const reached = new Promise<void>(resolve => { arrived = resolve }), gate = new Promise<void>(resolve => { release = resolve })
        const settled = new Promise<void>(resolve => { finished = resolve })
        await page.route(`**/api/approval/forms/${key}/${operation}`, async route => {
          const response = await route.fetch()
          expect(response.ok()).toBe(true)
          if (++calls === 1) await route.abort('failed')
          else { arrived(); await gate; await route.fulfill({ response }); finished() }
        })
        await open(page, framework, key)
        if (change === 'identity') {
          const saved = await request.put(`${api()}/forms/${key}/draft`, { headers: headers(), data: { draftId: null, formData: { subject: 'Original' }, comment: '' } })
          expect(saved.ok()).toBe(true)
          const { draftId } = await saved.json()
          await page.goto(page.url() + '&draft=' + draftId)
          await expect(page.getByRole('textbox', { name: /^\*?恢复主题$/ })).toHaveValue('Original')
        }
        await send(page, operation)
        await page.getByRole('button', { name: '按原内容重试上次操作', exact: true }).click()
        await reached
        if (change === 'identity') {
          if (framework === 'React') await chooseOption(page.getByRole('combobox', { name: '演示用户' }), 'bob')
          else {
            await page.locator('.user-switcher__select').click()
            await page.getByRole('option').filter({ hasText: /bob/i }).click()
          }
          await expect.poll(() => new URL(page.url()).searchParams.get('draft')).toBeNull()
          await expect(page.getByRole('textbox', { name: /^\*?恢复主题$/ })).toHaveValue('Original')
          expect(new URL(page.url()).searchParams.get('source')).toBe('workbench')
          expect(new URL(page.url()).searchParams.get('tab')).toBe('mine')
          await page.getByRole('textbox', { name: /^\*?恢复主题$/ }).fill('Bob owns this form')
        } else {
          if (framework === 'React') await page.getByRole('link', { name: '发起中心', exact: true }).click()
          else await page.getByRole('button', { name: '取消', exact: true }).click()
          await expect(page).toHaveURL(/\/start$/)
          if (change === 'flow') {
            if (framework === 'React') await page.getByRole('link', { name: other, exact: true }).click()
            else await page.locator('.start__card').filter({ hasText: other }).click()
            await expect(page).toHaveURL(new RegExp(`/start/${other}$`))
          }
        }
        const chosen = page.url()
        release()
        await settled
        await expect.poll(async () => (await records(request, key, operation)).length).toBe(1)
        await expect(page).toHaveURL(chosen)
        if (change === 'identity') {
          await expect(page.getByRole('textbox', { name: /^\*?恢复主题$/ })).toHaveValue('Bob owns this form')
          await expect(page.getByText('操作：' + `${operation}:${key}`, { exact: true })).toHaveCount(0)
        }
        expect(await pending(page, key, operation)).not.toBeNull()
      })
    }
  }
  for (const [label, operation, invalid] of [
    ['missing instance ID', 'start', {}], ['empty instance ID', 'start', { instanceId: '' }],
    ['missing draft ID', 'draft', { updatedAt: '2026-11-10T00:00:00Z' }],
    ['wrong draft ID type', 'draft', { draftId: 12, updatedAt: '2026-11-10T00:00:00Z' }],
    ['invalid draft time', 'draft', { draftId: 'bad', updatedAt: 'not-a-date' }],
  ] as const) {
    test(`${framework} rejects ${label} in the recovered response and keeps unrelated pending operations`, async ({ page, request }) => {
      const key = await define(request)
      await page.addInitScript(() => sessionStorage.setItem(`approval.intent.v1:${encodeURIComponent('alice:/api/approval:start-form')}:${encodeURIComponent('draft:unrelated')}`,
        JSON.stringify({ key: 'unrelated-key', fingerprint: '0'.repeat(64), uncertain: true })))
      let calls = 0
      const sent: { key?: string; body: string | null }[] = []
      await page.route(`**/api/approval/forms/${key}/${operation}`, async route => {
        sent.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() })
        const response = await route.fetch()
        expect(response.ok()).toBe(true)
        if (++calls === 1) await route.abort('failed')
        else await route.fulfill({ response, json: invalid })
      })
      await open(page, framework, key)
      await send(page, operation)
      await page.getByRole('button', { name: '按原内容重试上次操作', exact: true }).click()
      await expect(page.getByText(/恢复结果缺少有效/).first()).toBeVisible()
      expect(new URL(page.url()).pathname).toBe(`/start/${key}`)
      expect(new URL(page.url()).search).toBe('?source=workbench&tab=mine')
      expect(await pending(page, key, operation)).not.toBeNull()
      const other = await pending(page, 'unrelated', 'draft')
      expect(JSON.parse(other!)).toEqual({ key: 'unrelated-key', fingerprint: '0'.repeat(64), uncertain: true })
      expect(sent).toEqual([sent[0], sent[0]])
      expect(await records(request, key, operation)).toHaveLength(1)
      await expect(page.locator(framework === 'Vue' ? '.start-form__saved' : '.ar-save-state')).not.toContainText(framework === 'Vue' ? '草稿已保存' : '最后保存')
    })
  }
}

const tenantBase = { A: '/api/tenant-a/approval', B: '/api/tenant-b/approval' }
const tenantApiUrl = (tenant: 'A' | 'B') => (tenant === 'A' ? process.env.APPROVAL_SMOKE_API! : process.env.APPROVAL_TENANT_B_API!) + '/api/approval'
async function tenantDefinition(request: APIRequestContext, key: string, tenant: 'A' | 'B') {
  const response = await request.post(`${tenantApiUrl(tenant)}/definitions/${key}?name=${tenant} tenant form`, { headers: headers('admin'), data: {
    schemaVersion: 1, startNodeId: 'start', settings: { startable: true },
    form: { fields: [{ key: 'subject', label: '隔离主题', type: 'Text', required: true, defaultValue: tenant + ' default' }] },
    nodes: { start: { id: 'start', name: '开始', type: 'Start', next: 'review' },
      review: { id: 'review', name: '审批', type: 'Approval', approvers: [{ type: 'User', value: 'bob' }], next: 'end' },
      end: { id: 'end', name: '结束', type: 'End' } },
  } })
  expect(response.ok(), await response.text()).toBe(true)
}
function hostQuery(page: Page) { return new URLSearchParams(new URL(page.url()).hash.split('?')[1]) }
async function openTenantDraft(page: Page, request: APIRequestContext) {
  const key = 'api-base-' + crypto.randomUUID()
  await tenantDefinition(request, key, 'A'); await tenantDefinition(request, key, 'B')
  const saved = await request.put(`${tenantApiUrl('A')}/forms/${key}/draft`, { headers: headers(), data: {
    draftId: null, formData: { subject: 'A private draft' }, comment: 'A private note',
  } })
  expect(saved.ok()).toBe(true)
  const { draftId } = await saved.json()
  await page.goto(`/host.html?tenant=A#/embedded/start/${key}?draft=${draftId}&source=workbench&tab=mine`)
  await expect(page.getByRole('textbox', { name: '隔离主题', exact: true })).toHaveValue('A private draft')
  return { key, draftId }
}
async function assertTenantB(page: Page) {
  await expect(page.getByRole('textbox', { name: '隔离主题', exact: true })).toHaveValue('B default')
  expect(hostQuery(page).get('draft')).toBeNull()
  expect(hostQuery(page).get('source')).toBe('workbench')
  expect(hostQuery(page).get('tab')).toBe('mine')
  await expect(page.getByRole('textbox', { name: '申请说明', exact: true })).toHaveValue('')
  await expect(page.locator('.ar-save-state')).not.toContainText('最后保存')
}
test('same user API Base switch isolates an existing draft and fetches only tenant B form and permissions', async ({ page, request }) => {
  const { key, draftId } = await openTenantDraft(page, request)
  const urls: string[] = []
  page.on('request', current => { urls.push(current.url()) })
  await page.getByRole('combobox', { name: '宿主 API Base', exact: true }).selectOption(tenantBase.B)
  await assertTenantB(page)
  await expect(page.getByRole('combobox', { name: /^宿主身份/ })).toHaveValue('alice')
  expect(urls.some(url => url.includes(tenantBase.B + '/forms/drafts/' + draftId))).toBe(false)
  expect(urls.some(url => url.endsWith(tenantBase.B + '/me'))).toBe(true)
  await page.getByRole('textbox', { name: '隔离主题', exact: true }).fill('B saved separately')
  const saved = page.waitForResponse(response => response.url().endsWith(tenantBase.B + `/forms/${key}/draft`) && response.request().method() === 'PUT')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await saved).ok()).toBe(true)
  const a = await request.get(`${tenantApiUrl('A')}/forms/drafts/${draftId}`, { headers: headers() })
  expect(JSON.parse((await a.json()).formDataJson).subject).toBe('A private draft')
  const b = await request.get(`${tenantApiUrl('B')}/forms/drafts`, { headers: headers() })
  expect((await b.json()).filter((draft: { definitionKey: string }) => draft.definitionKey === key)).toHaveLength(1)
})
for (const operation of ['start', 'draft'] as const) {
  for (const combined of [false, true]) {
    for (const recovering of [false, true]) {
    test(`late tenant A ${operation} ${recovering ? 'recovery' : 'response'} stays isolated after API Base ${combined ? 'and user' : 'only'} changes`, async ({ page, request }) => {
      const { key, draftId } = await openTenantDraft(page, request)
      let release!: () => void, arrived!: () => void, finished!: () => void, originalKey = '', calls = 0
      const postedKeys: string[] = []
      const reached = new Promise<void>(resolve => { arrived = resolve }), gate = new Promise<void>(resolve => { release = resolve }), settled = new Promise<void>(resolve => { finished = resolve })
      await page.route(`**${tenantBase.A}/forms/${key}/${operation}`, async route => {
        originalKey = route.request().headers()['idempotency-key']!
        postedKeys.push(originalKey)
        const response = await route.fetch()
        expect(response.ok()).toBe(true)
        if (recovering && ++calls === 1) { await route.abort('failed'); return }
        arrived(); await gate; await route.fulfill({ response }); finished()
      })
      await page.getByRole('button', { name: operation === 'start' ? '提交申请' : '保存草稿', exact: true }).click()
      if (recovering) await page.getByRole('button', { name: '按原内容重试上次操作', exact: true }).click()
      await reached
      const urls: string[] = []; page.on('request', current => { urls.push(current.url()) })
      if (combined) await page.getByRole('button', { name: '切换身份和 API Base', exact: true }).click()
      else await page.getByRole('combobox', { name: '宿主 API Base', exact: true }).selectOption(tenantBase.B)
      await assertTenantB(page)
      await page.getByRole('textbox', { name: '隔离主题', exact: true }).fill('B edited')
      const chosen = page.url()
      release(); await settled
      await expect(page).toHaveURL(chosen)
      await expect(page.getByRole('textbox', { name: '隔离主题', exact: true })).toHaveValue('B edited')
      await expect(page.locator('.ar-save-state')).not.toContainText('最后保存')
      expect(urls.some(url => url.includes(tenantBase.B + '/forms/drafts/' + draftId))).toBe(false)
      const record = await page.evaluate(({ key, operation, base }) => sessionStorage.getItem(`approval.intent.v1:${encodeURIComponent(`alice:${base}:start-form`)}:${encodeURIComponent(`${operation}:${key}`)}`), { key, operation, base: tenantBase.A })
      expect(JSON.parse(record!).key).toBe(originalKey)
      expect(postedKeys).toEqual(Array.from({ length: recovering ? 2 : 1 }, () => originalKey))
      expect(record).not.toContain('A private note')
      const b = await request.get(`${tenantApiUrl('B')}/forms/drafts`, { headers: headers(combined ? 'bob' : 'alice') })
      expect((await b.json()).filter((draft: { definitionKey: string }) => draft.definitionKey === key)).toHaveLength(0)
    })
    }
  }
}
