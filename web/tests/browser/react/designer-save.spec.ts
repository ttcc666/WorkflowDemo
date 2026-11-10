import { expect, test } from '@playwright/test'

for (const surface of ['React', 'Vue']) {
  test(`${surface} refuses a missing historical version and leaves the existing draft unchanged`, async ({ page, request }) => {
    const key = 'history-missing-' + crypto.randomUUID()
    const api = process.env.APPROVAL_SMOKE_API! + '/api/approval'
    const headers = { 'X-User-Id': 'admin', 'Idempotency-Key': crypto.randomUUID() }
    const created = await request.put(`${api}/definitions/${key}/draft`, { headers, data: {
      name: '需要保留的草稿', expectedRevision: 0, sourceDefinitionId: null,
      definitionJson: JSON.stringify({ schemaVersion: 1, startNodeId: 'start', nodes: {
        start: { id: 'start', name: '原开始节点', type: 'Start', next: 'end' }, end: { id: 'end', name: '原结束节点', type: 'End' },
      }, form: { fields: [{ key: 'subject', label: '原字段', type: 'Text' }] } }),
    } })
    expect(created.ok()).toBe(true)
    const before = await created.json()
    let writes = 0
    page.on('request', call => { if (call.method() === 'PUT' && call.url().endsWith(`/definitions/${key}/draft`)) writes++ })
    await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
    const missing = '00000000-0000-0000-0000-000000000000'
    const missingResponse = page.waitForResponse(response => response.url().includes('/definitions/by-id/' + missing))
    await page.goto((surface === 'Vue' ? process.env.APPROVAL_VUE_URL! : '') + `/designer/${key}?from=${missing}`)
    expect((await missingResponse).status()).toBe(404)
    const errors = surface === 'Vue' ? page.locator('.designer .el-alert--error') : page.locator('.ar-studio-page [role="alert"]')
    await expect(errors).toBeVisible()
    await expect(surface === 'Vue' ? page.getByPlaceholder('流程名称', { exact: true }) : page.getByRole('textbox', { name: '流程名称', exact: true })).toBeDisabled()
    await expect(page.getByRole('button', { name: '保存草稿', exact: true })).toBeDisabled()
    const after = await request.get(`${api}/definitions/${key}/governance`, { headers })
    expect(after.ok()).toBe(true)
    expect(await after.json()).toMatchObject({ revision: before.revision, draftJson: before.draftJson, name: before.name })
    expect(writes).toBe(0)
  })
}

test('a pending or failed designer load disables editing and saving without creating a replacement draft', async ({ page }) => {
  const key = 'load-failed-' + crypto.randomUUID()
  let release!: () => void
  let arrived!: () => void
  const gate = new Promise<void>(resolve => { release = resolve })
  const reached = new Promise<void>(resolve => { arrived = resolve })
  let writes = 0
  page.on('request', request => { if (request.method() === 'PUT' && request.url().endsWith(`/definitions/${key}/draft`)) writes++ })
  await page.route(`**/api/approval/definitions/${key}/governance`, async route => {
    arrived(); await gate; await route.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ title: '定义加载失败' }) })
  })
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  await page.goto(`/designer/${key}`)
  await reached
  await expect(page.getByRole('textbox', { name: '流程名称', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '保存草稿', exact: true })).toBeDisabled()
  release()
  await expect(page.getByRole('alert').filter({ hasText: '定义加载失败' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '流程名称', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '保存草稿', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '添加审批', exact: true })).toHaveCount(0)
  expect(writes).toBe(0)
})

test('designer name-only edits block leaving and edits made during a real save remain unsaved', async ({ page, request }) => {
  const key = 'save-race-' + crypto.randomUUID()
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  await page.goto(`/designer/${key}?name=initial`)
  const name = page.getByRole('textbox', { name: '流程名称', exact: true })
  await expect(page.getByRole('button', { name: '保存草稿', exact: true })).toBeEnabled()
  await name.fill('实际提交的名称')
  await page.getByRole('link', { name: '← 流程定义' }).click()
  await expect(page.getByRole('alertdialog', { name: '离开设计器' })).toBeVisible()
  await page.getByRole('alertdialog').getByRole('button', { name: '取消', exact: true }).click()
  let release!: () => void
  let arrived!: () => void
  const blocked = new Promise<void>(resolve => { release = resolve })
  const reached = new Promise<void>(resolve => { arrived = resolve })
  await page.route(`**/api/approval/definitions/${key}/draft`, async route => {
    arrived(); await blocked; await route.continue()
  })
  const saved = page.waitForResponse(response => response.request().method() === 'PUT' && response.url().endsWith(`/definitions/${key}/draft`))
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await reached
  await name.fill('保存期间继续编辑的名称')
  release()
  expect((await saved).ok()).toBe(true)
  await expect(page.getByText('设计草稿已保存', { exact: true })).toBeVisible()
  await expect(name).toHaveValue('保存期间继续编辑的名称')
  await expect(page.getByText('未保存', { exact: true })).toBeVisible()
  const result = await request.get(process.env.APPROVAL_SMOKE_API! + `/api/approval/definitions/${key}/governance`, { headers: { 'X-User-Id': 'admin' } })
  expect(result.ok()).toBe(true)
  expect((await result.json()).name).toBe('实际提交的名称')
  await page.getByRole('link', { name: '← 流程定义' }).click()
  await expect(page.getByRole('alertdialog', { name: '离开设计器' })).toBeVisible()
})

test('Vue name-only edits block leaving and a real save preserves a newer name and its dirty state', async ({ page, request }) => {
  const key = 'vue-save-race-' + crypto.randomUUID()
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  await page.goto(process.env.APPROVAL_VUE_URL! + `/designer/${key}?name=initial`)
  const name = page.getByPlaceholder('流程名称', { exact: true })
  await expect(name).toBeEnabled()
  await name.fill('Vue实际提交的名称')
  await page.getByRole('button', { name: '← 返回', exact: true }).click()
  const leaving = page.getByRole('dialog').filter({ hasText: '有未保存的修改' })
  await expect(leaving).toBeVisible()
  await leaving.getByRole('button', { name: '取消', exact: true }).click()
  let release!: () => void
  let arrived!: () => void
  const blocked = new Promise<void>(resolve => { release = resolve })
  const reached = new Promise<void>(resolve => { arrived = resolve })
  await page.route(`**/api/approval/definitions/${key}/draft`, async route => { arrived(); await blocked; await route.continue() })
  const saved = page.waitForResponse(response => response.request().method() === 'PUT' && response.url().endsWith(`/definitions/${key}/draft`))
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await reached
  await name.fill('Vue保存期间的新名称')
  release()
  expect((await saved).ok()).toBe(true)
  await expect(name).toHaveValue('Vue保存期间的新名称')
  await expect(page.getByText('未发布的修改', { exact: true })).toBeVisible()
  const result = await request.get(process.env.APPROVAL_SMOKE_API! + `/api/approval/definitions/${key}/governance`, { headers: { 'X-User-Id': 'admin' } })
  expect(result.ok()).toBe(true)
  expect((await result.json()).name).toBe('Vue实际提交的名称')
})
