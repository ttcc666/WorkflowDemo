import { expect, test } from '@playwright/test'

for (const framework of ['React', 'Vue']) {
  test(`${framework} refresh discovers all opaque recovery records and review clears only one without a write`, async ({ page }) => {
    await page.addInitScript(() => {
      localStorage.setItem('approval-demo-user', 'admin')
      if (sessionStorage.getItem('p2-seeded')) return
      for (const scope of ['first-op', 'second-op']) {
        sessionStorage.setItem(`approval.intent.v1:${encodeURIComponent('admin:/api/approval:outbox-retry')}:${encodeURIComponent(scope)}`,
          JSON.stringify({ key: crypto.randomUUID(), fingerprint: '0'.repeat(64), uncertain: true }))
      }
      sessionStorage.setItem('p2-seeded', 'true')
    })
    const writes: string[] = []
    page.on('request', request => { if (['POST', 'PUT', 'DELETE'].includes(request.method()) && request.url().includes('/api/approval/')) writes.push(request.url()) })
    await page.goto((framework === 'Vue' ? process.env.APPROVAL_VUE_URL! : '') + '/outbox')
    await expect(page.getByText('操作：first-op', { exact: true })).toBeVisible()
    await expect(page.getByText('操作：second-op', { exact: true })).toBeVisible()
    await page.reload()
    await expect(page.getByText('操作：first-op', { exact: true })).toBeVisible()
    await expect(page.getByText('操作：second-op', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '按原内容重试上次操作', exact: true })).toHaveCount(0)
    const row = page.locator(framework === 'Vue' ? '.el-alert--warning' : '[data-slot="card"]').filter({ has: page.getByText('操作：first-op', { exact: true }) })
    await row.getByRole('button', { name: '核对结果后开始新操作', exact: true }).click()
    const confirm = framework === 'Vue' ? page.getByRole('dialog').filter({ hasText: '确认已核对原操作结果' }) : page.getByRole('alertdialog', { name: '开始新操作' })
    await confirm.getByRole('button', { name: framework === 'Vue' ? '确定' : '确认', exact: true }).click()
    await expect(page.getByText('操作：first-op', { exact: true })).toHaveCount(0)
    await expect(page.getByText('操作：second-op', { exact: true })).toBeVisible()
    await page.reload()
    await expect(page.getByText('操作：first-op', { exact: true })).toHaveCount(0)
    await expect(page.getByText('操作：second-op', { exact: true })).toBeVisible()
    expect(writes).toEqual([])
  })
}

test('a real committed unread-message request cannot overwrite navigation chosen in the global layout', async ({ page, request }) => {
  const api = process.env.APPROVAL_SMOKE_API! + '/api/approval'
  const key = 'message-nav-' + crypto.randomUUID(), title = '导航保护-' + crypto.randomUUID()
  const definition = await request.post(`${api}/definitions/${key}?name=${key}`, { headers: { 'X-User-Id': 'admin', 'Idempotency-Key': crypto.randomUUID() }, data: {
    schemaVersion: 1, startNodeId: 'start', settings: { startable: true }, form: { fields: [{ key: 'note', label: '说明', type: 'Text' }] },
    nodes: {
      start: { id: 'start', name: '开始', type: 'Start', next: 'notify' },
      notify: { id: 'notify', name: '站内信', type: 'Action', next: 'review', actions: [{ id: 'notice', type: 'Notification', mode: 'Sync', configVersion: 1, failurePolicy: 'Fail', config: {
        recipients: [{ type: 'Initiator', value: '' }], channels: ['InApp'], title, content: '实际审批消息',
      } }] },
      review: { id: 'review', name: '审批', type: 'Approval', approvers: [{ type: 'User', value: 'bob' }], next: 'end' },
      end: { id: 'end', name: '结束', type: 'End' },
    },
  } })
  expect(definition.ok(), await definition.text()).toBe(true)
  const started = await request.post(`${api}/forms/${key}/start`, { headers: { 'X-User-Id': 'alice', 'Idempotency-Key': crypto.randomUUID() }, data: { formData: { note: title }, comment: '' } })
  expect(started.ok(), await started.text()).toBe(true)
  let messageId = ''
  await expect.poll(async () => {
    const response = await request.get(`${api}/messages`, { headers: { 'X-User-Id': 'alice' } })
    const messages = await response.json() as { id: string; title: string }[]
    messageId = messages.find(message => message.title === title)?.id ?? ''
    return messageId.length > 0
  }).toBe(true)
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'alice'))
  await page.goto('/workbench')
  let release!: () => void, arrived!: () => void
  const gate = new Promise<void>(resolve => { release = resolve }), reached = new Promise<void>(resolve => { arrived = resolve })
  await page.route(`**/api/approval/messages/${messageId}/read`, async route => {
    const response = await route.fetch()
    expect(response.ok()).toBe(true)
    arrived(); await gate; await route.fulfill({ response })
  })
  await page.getByRole('button', { name: '消息通知', exact: true }).click()
  const completed = page.waitForResponse(response => response.url().endsWith(`/messages/${messageId}/read`) && response.request().method() === 'POST')
  await page.getByRole('button', { name: title, exact: true }).click()
  await reached
  await page.getByRole('dialog', { name: '消息通知', exact: true }).getByRole('button', { name: '关闭', exact: true }).click()
  await page.getByRole('link', { name: '注册证业务', exact: true }).click()
  await expect(page).toHaveURL(/\/certificates$/)
  await expect(page.getByRole('heading', { name: '注册证业务', exact: true })).toBeVisible()
  const chosen = page.url()
  release(); expect((await completed).ok()).toBe(true)
  const unread = await request.get(`${api}/messages/unread-count`, { headers: { 'X-User-Id': 'alice' } })
  expect(unread.ok()).toBe(true)
  const { count } = await unread.json() as { count: number }
  await expect(page.getByRole('button', { name: '消息通知', exact: true })).toHaveText(new RegExp(`^消息\\s*${count}$`))
  await expect(page).toHaveURL(chosen)
  const result = await request.get(`${api}/messages`, { headers: { 'X-User-Id': 'alice' } })
  expect((await result.json() as { id: string; isRead: boolean }[]).find(message => message.id === messageId)?.isRead).toBe(true)
})
