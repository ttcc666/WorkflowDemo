import { expect, test, type Page, type APIRequestContext } from '@playwright/test'

const users: Record<string, RegExp> = {
  admin: /管理员/, alice: /Alice（员工）/, dave: /Dave（研发部负责人）/, quinn: /Quinn（质量）/, leo: /Leo（负责人）/,
}

async function switchUser(page: Page, id: string) {
  const me = page.waitForResponse(response => new URL(response.url()).pathname === '/api/approval/me' && response.status() === 200)
  await page.locator('.user-switcher__select').click()
  await page.getByRole('option').filter({ hasText: users[id]! }).click()
  expect((await (await me).json()).userId).toBe(id)
}

async function json(request: APIRequestContext, path: string, user = 'alice') {
  const response = await request.get(process.env.APPROVAL_SMOKE_API + path, { headers: { 'X-User-Id': user } })
  expect(response.ok(), await response.text()).toBe(true)
  return response.json()
}

test('packaged designer publishes a workflow and three approvals activate the certificate', async ({ page, request }, info) => {
  test.setTimeout(120000)
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  await page.addInitScript(() => {
    if (!localStorage.getItem('approval-demo-user')) localStorage.setItem('approval-demo-user', 'admin')
  })
  const before = await json(request, '/api/approval/definitions/certificate.create', 'admin')
  await page.goto('/designer/certificate.create')
  await expect(page.getByPlaceholder('流程名称')).toBeVisible()
  const node = page.locator('.vue-flow__node[data-id="dept"]')
  await expect(node).toBeVisible()
  expect(await node.evaluate(element => getComputedStyle(element).position)).toBe('absolute')
  await node.click()
  const name = page.getByRole('complementary').last().getByRole('textbox').first()
  await expect(name).toHaveValue(JSON.parse(before.definitionJson).nodes.dept.name)
  await name.fill('包验收初审')
  await page.getByPlaceholder('流程名称').fill('打包注册证验收')
  const published = page.waitForResponse(response => new URL(response.url()).pathname === '/api/approval/definitions/certificate.create' && response.request().method() === 'POST')
  await page.getByRole('button', { name: '发布新版本', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '确定', exact: true }).click()
  const publication = await published
  expect(publication.status()).toBe(200)
  const version = (await publication.json()).version
  expect(version).toBe(before.version + 1)
  expect(publication.request().postDataJSON()).toMatchObject({ schemaVersion: 1, nodes: { dept: { name: '包验收初审' } } })

  await switchUser(page, 'alice')
  await page.getByRole('menuitem', { name: '证书管理', exact: true }).click()
  await page.getByRole('button', { name: '登记注册证', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '申请登记注册证' })
  const code = 'PKG-' + Date.now()
  await dialog.getByLabel('注册证编号', { exact: true }).fill(code)
  await dialog.getByLabel('注册证名称', { exact: true }).fill('独立包验证注册证')
  await dialog.getByLabel('持有人', { exact: true }).fill('包验收企业')
  await dialog.getByLabel('发证日期', { exact: true }).fill('2026-01-01')
  await dialog.getByLabel('发证日期', { exact: true }).press('Enter')
  await dialog.getByLabel('到期日期', { exact: true }).fill('2030-12-31')
  await dialog.getByLabel('到期日期', { exact: true }).press('Enter')
  const submitted = page.waitForResponse(response => new URL(response.url()).pathname === '/api/certificates' && response.request().method() === 'POST')
  await dialog.getByRole('button', { name: '提交审核', exact: true }).click()
  const submission = await submitted
  expect(submission.status()).toBe(202)
  const requestId = (await submission.json()).requestId
  await expect(page).toHaveURL(/\/approvals\//)
  const detail = await json(request, '/api/certificate-requests/' + requestId)
  const instanceId = detail.approval.id
  expect(detail.approval.definitionVersion).toBe(version)
  expect(detail.approval.tasks[0].nodeName).toBe('包验收初审')
  expect((await json(request, '/api/certificates?keyword=' + code)).total).toBe(0)
  await expect(page.getByRole('button', { name: '同意', exact: true })).toHaveCount(0)

  for (const reviewer of ['dave', 'quinn', 'leo']) {
    await switchUser(page, reviewer)
    await page.goto('/approvals/' + instanceId)
    await page.getByRole('button', { name: '同意', exact: true }).click()
    const approvalDialog = page.getByRole('dialog')
    await approvalDialog.getByRole('textbox').fill('包验收通过 ' + reviewer)
    const approved = page.waitForResponse(response => /\/tasks\/[^/]+\/approve$/.test(new URL(response.url()).pathname) && response.request().method() === 'POST')
    await approvalDialog.getByRole('button', { name: '确定', exact: true }).click()
    expect((await approved).status()).toBe(204)
    if (reviewer !== 'leo') expect((await json(request, '/api/certificates?keyword=' + code)).total).toBe(0)
  }
  const completed = await json(request, '/api/certificate-requests/' + requestId)
  expect(completed.request.status).toBe('Approved')
  expect(completed.approval.status).toBe('Approved')
  const certificates = await json(request, '/api/certificates?keyword=' + code)
  expect(certificates.total).toBe(1)
  expect(certificates.items[0]).toMatchObject({ certNo: code, name: '独立包验证注册证', holder: '包验收企业', status: 'Active', effectiveStatus: 'Valid' })
  await switchUser(page, 'alice')
  await page.getByRole('menuitem', { name: '证书管理', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: code })).toContainText('有效')
  await page.screenshot({ path: info.outputPath('approved-certificate.png'), fullPage: true, animations: 'disabled' })
  await info.attach('browser-version', { body: page.context().browser()!.version(), contentType: 'text/plain' })
  expect(errors).toEqual([])
})

test('packaged endpoints reject anonymous access and unsupported protocol versions', async ({ request }) => {
  const api = process.env.APPROVAL_SMOKE_API!
  expect((await request.get(api + '/api/approval/todo')).status()).toBe(401)
  const invalid = await request.post(api + '/api/approval/definitions/unsupported.package', {
    headers: { 'X-User-Id': 'admin' },
    data: { schemaVersion: 2, startNodeId: 'start', nodes: { start: { type: 'Start', next: 'end' }, end: { type: 'End' } } },
  })
  expect(invalid.status()).toBe(400)
  expect((await invalid.json()).title).toContain('协议版本 2')
  expect((await request.get(api + '/api/approval/definitions/unsupported.package', { headers: { 'X-User-Id': 'admin' } })).status()).toBe(404)
})
