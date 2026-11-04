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
  await page.getByRole('button', { name: '草稿预览', exact: true }).click()
  const draftPreview = page.getByRole('dialog', { name: '草稿预览', exact: true })
  await expect(draftPreview).toBeVisible()
  await expect(draftPreview.getByText('包验收初审', { exact: true })).toBeVisible()
  await draftPreview.locator('.el-dialog__headerbtn').click()
  const saved = page.waitForResponse(response => new URL(response.url()).pathname === '/api/approval/definitions/certificate.create/draft' && response.request().method() === 'PUT')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  const draftResponse = await saved
  expect(draftResponse.status()).toBe(200)
  const savedRevision = (await draftResponse.json()).revision
  await page.reload()
  await expect(page.getByPlaceholder('流程名称')).toHaveValue('打包注册证验收')
  await page.locator('.vue-flow__node[data-id="dept"]').click()
  await expect(page.getByRole('complementary').last().getByRole('textbox').first()).toHaveValue('包验收初审')
  const published = page.waitForResponse(response => new URL(response.url()).pathname === '/api/approval/definitions/certificate.create/publish-draft' && response.request().method() === 'POST')
  await page.getByRole('button', { name: '发布新版本', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '确定', exact: true }).click()
  const publication = await published
  expect(publication.status()).toBe(200)
  const version = (await publication.json()).version
  expect(version).toBe(before.version + 1)
  expect(publication.request().postDataJSON()).toMatchObject({ expectedRevision: savedRevision, changeNote: '' })
  const current = await json(request, '/api/approval/definitions/certificate.create', 'admin')
  expect(JSON.parse(current.definitionJson)).toMatchObject({ schemaVersion: 1, nodes: { dept: { name: '包验收初审' } } })

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
  await expect(page).toHaveURL(/\/certificates$/)
  const certificateRow = page.getByRole('row').filter({ has: page.getByRole('cell', { name: code, exact: true }) })
  await expect(certificateRow).toHaveCount(1)
  await expect(certificateRow.getByRole('cell', { name: '有效', exact: true })).toBeVisible()
  await page.screenshot({ path: info.outputPath('approved-certificate.png'), fullPage: true, animations: 'disabled' })
  await info.attach('browser-version', { body: page.context().browser()!.version(), contentType: 'text/plain' })
  expect(errors).toEqual([])
})

test('packaged endpoints reject anonymous access and unsupported protocol versions', async ({ request }) => {
  const api = process.env.APPROVAL_SMOKE_API!
  expect((await request.get(api + '/api/approval/todo')).status()).toBe(401)
  const invalid = await request.post(api + '/api/approval/definitions/unsupported.package', {
    headers: { 'X-User-Id': 'admin', 'Idempotency-Key': 'unsupported-package-protocol' },
    data: { schemaVersion: 2, startNodeId: 'start', nodes: { start: { type: 'Start', next: 'end' }, end: { type: 'End' } } },
  })
  expect(invalid.status()).toBe(400)
  expect((await invalid.json()).title).toContain('协议版本 2')
  expect((await request.get(api + '/api/approval/definitions/unsupported.package', { headers: { 'X-User-Id': 'admin' } })).status()).toBe(404)
})
test('packaged diagnostics expose anonymous readiness only and removed maintenance routes return 404', async ({ page, request }) => {
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  const response = await request.get(process.env.APPROVAL_SMOKE_API + '/health/approval')
  expect(response.status()).toBe(200); expect(await response.json()).toEqual({ ready: true })
  await page.goto('/diagnostics')
  await expect(page.getByText('数据库与迁移已就绪', { exact: true })).toBeVisible()
  for (const path of ['/api/approval/maintenance/preview', '/api/approval/maintenance/jobs', '/api/approval/archives']) {
    expect((await request.get(process.env.APPROVAL_SMOKE_API + path, { headers: { 'X-User-Id': 'admin' } })).status()).toBe(404)
  }
  expect((await request.get(process.env.APPROVAL_SMOKE_API + '/api/approval/diagnostics')).status()).toBe(401)
})

test('packaged returned form can be edited and resubmitted and carbon copy read state stays separate', async ({ page, request }) => {
  test.setTimeout(120000)
  const api = process.env.APPROVAL_SMOKE_API!
  const key = `package.return.${Date.now()}`
  const flow = { schemaVersion: 1, startNodeId: 'start', settings: { startable: true, titleTemplate: '打包退回重提' },
    form: { fields: [{ key: 'memo', label: '说明', type: 'Text', required: true }] },
    nodes: { start: { type: 'Start', name: '申请', next: 'cc' },
      cc: { type: 'Cc', name: '抄送人事', approvers: [{ type: 'Role', value: 'HR' }], next: 'review' },
      review: { type: 'Approval', name: '审核', approvers: [{ type: 'User', value: 'dave' }], requireComment: true, next: 'end' },
      end: { type: 'End', name: '结束' } } }
  const published = await request.post(api + `/api/approval/definitions/${key}?name=打包退回重提`, { headers: { 'X-User-Id': 'admin', 'Idempotency-Key': key }, data: flow })
  expect(published.ok(), await published.text()).toBe(true)
  const started = await request.post(api + `/api/approval/forms/${key}/start`, { headers: { 'X-User-Id': 'alice', 'Idempotency-Key': `${key}-start` }, data: { formData: { memo: '修改前' } } })
  expect(started.ok(), await started.text()).toBe(true)
  const id = (await started.json()).instanceId
  const task = (await json(request, `/api/approval/instances/${id}/my-task`, 'dave')).task
  const returned = await request.post(api + `/api/approval/tasks/${task.id}/reject`, { headers: { 'X-User-Id': 'dave', 'Idempotency-Key': `${key}-return` }, data: { targetNodeId: 'start', comment: '补充说明' } })
  expect(returned.status()).toBe(204)
  await page.addInitScript(() => { if (!localStorage.getItem('approval-demo-user')) localStorage.setItem('approval-demo-user', 'alice') })
  await page.goto(`/approvals/${id}`)
  await expect(page.getByText('待申请人修改', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '重新提交', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '重新提交' })
  await dialog.locator('.el-form-item').filter({ hasText: '说明' }).getByRole('textbox').fill('修改后补充内容')
  const submitted = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/resubmit') && r.request().method() === 'POST')
  await dialog.getByRole('button', { name: '确定', exact: true }).click()
  expect((await submitted).ok()).toBe(true)
  await expect(page.getByText('审批中', { exact: true })).toBeVisible()
  const detail = await json(request, `/api/approval/instances/${id}`)
  expect(JSON.parse(detail.formDataJson).memo).toBe('修改后补充内容')
  expect(detail.logs.filter((l: { action: string }) => l.action === 'Resubmit')).toHaveLength(1)
  const copies = await json(request, `/api/approval/carbon-copies?businessKey=${detail.businessKey}&unreadOnly=true`, 'hana')
  expect(copies.total).toBe(2)
  await page.evaluate(() => localStorage.setItem('approval-demo-user', 'hana'))
  await page.goto('/workbench?tab=cc')
  const row = page.getByRole('row').filter({ hasText: '打包退回重提' }).first()
  await expect(row.getByText('未读', { exact: true })).toBeVisible()
  await row.getByRole('button', { name: '查看人员', exact: true }).click()
  const people = page.getByRole('dialog', { name: '抄送接收人员' })
  await expect(people.getByText('以下为宿主当前角色成员，不是抄送发生时的历史人员快照。', { exact: true })).toBeVisible()
  await expect(people.getByRole('cell', { name: 'Hana（人事）', exact: true })).toBeVisible()
  expect((await json(request, `/api/approval/carbon-copies?businessKey=${detail.businessKey}&unreadOnly=true`, 'hana')).total).toBe(2)
  await people.locator('.el-dialog__headerbtn').click()
  await row.click()
  await expect(page).toHaveURL(new RegExp(`/approvals/${id}$`))
  const after = await json(request, `/api/approval/carbon-copies?businessKey=${detail.businessKey}&unreadOnly=true`, 'hana')
  expect(after.total).toBe(1)
})


test('packaged definition categories are shown and filtered by server', async ({ page, request }) => {
  const api = process.env.APPROVAL_SMOKE_API!, prefix = `package.category.${Date.now()}`
  const category = `分类验收-${Date.now()}`
  const flow = (value: string) => ({ schemaVersion: 1, settings: { category: value }, startNodeId: 's', nodes: { s: { type: 'Start', name: '申请', next: 'e' }, e: { type: 'End', name: '结束' } } })
  for (const [suffix, value] of [['one', category], ['two', category + '-other']]) {
    const response = await request.post(api + `/api/approval/definitions/${prefix}.${suffix}?name=${suffix}`, { headers: { 'X-User-Id': 'admin', 'Idempotency-Key': `${prefix}.${suffix}` }, data: flow(value!) })
    expect(response.ok(), await response.text()).toBe(true)
  }
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  await page.goto('/definitions')
  await expect(page.getByRole('row').filter({ hasText: `${prefix}.one` })).toBeVisible()
  await page.locator('.category-filter .el-select').click()
  const filtered = page.waitForResponse(r => { const url = new URL(r.url()); return url.pathname === '/api/approval/definitions/governance' && url.searchParams.get('category') === category })
  await page.getByRole('option', { name: category, exact: true }).click()
  const response = await filtered
  expect((await response.json()).map((d: { key: string }) => d.key)).toEqual([`${prefix}.one`])
  await expect(page.locator('.el-table__body-wrapper .el-table__row')).toHaveCount(1)
  await expect(page.getByRole('cell', { name: category, exact: true })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: `${prefix}.two` })).toHaveCount(0)
})
