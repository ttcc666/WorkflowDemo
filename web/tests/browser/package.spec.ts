import { expect, test, type Page, type APIRequestContext } from '@playwright/test'
import { readFile } from 'node:fs/promises'

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

async function publishPortableVersions(request: APIRequestContext, key: string) {
  const endpoint = `${process.env.APPROVAL_SMOKE_API}/api/approval/definitions/${key}`
  for (const [index, name] of ['初审', '复审'].entries()) {
    const response = await request.post(endpoint, {
      headers: { 'X-User-Id': 'admin', 'Idempotency-Key': `${key}.${index}` },
      data: { schemaVersion: 1, startNodeId: 's', nodes: {
        s: { type: 'Start', name: '提交', next: 'review' },
        review: { type: 'Approval', name, approvers: [{ type: 'User', value: 'dave' }], next: 'e' },
        e: { type: 'End', name: '结束' },
      } },
    })
    expect(response.ok(), await response.text()).toBe(true)
  }
}

async function openVersions(page: Page, key: string) {
  await page.addInitScript(() => { if (!localStorage.getItem('approval-demo-user')) localStorage.setItem('approval-demo-user', 'admin') })
  await page.goto('/definitions')
  await page.getByRole('row').filter({ hasText: key }).getByRole('button', { name: '版本与治理', exact: true }).click()
  return page.getByRole('dialog', { name: `版本与治理 · ${key}` })
}

test('packaged version comparison swaps and locates changes without changing the active version', async ({ page, request }) => {
  const key = `package.compare.${Date.now()}`
  await publishPortableVersions(request, key)
  const before = await json(request, `/api/approval/definitions/${key}/governance`, 'admin')
  const drawer = await openVersions(page, key)
  await drawer.getByRole('button', { name: '版本对比', exact: true }).click()
  const comparison = drawer.getByRole('region', { name: '流程版本对比' })
  const compare = comparison.getByRole('button', { name: '比较版本', exact: true })
  await expect(compare).toBeDisabled()
  await comparison.getByText('选择基准版本', { exact: true }).click()
  await page.getByRole('option', { name: /^v1 ·/ }).click()
  await comparison.getByRole('button', { name: '目标选最新' }).click()
  await compare.click()
  const change = comparison.getByRole('row').filter({ hasText: '/nodes/review/name' })
  await expect(change.getByRole('cell').nth(1)).toHaveText('初审')
  await expect(change.getByRole('cell').nth(2)).toHaveText('复审')
  await change.getByRole('button', { name: '复审', exact: true }).click()
  await expect(comparison.locator('.vue-flow__node[data-id="review"] .flow-node--selected')).toBeVisible()
  await comparison.getByText('全部变化类型', { exact: true }).click()
  await page.getByRole('option', { name: '新增', exact: true }).click()
  await expect(change).toHaveCount(0)
  await comparison.getByRole('button', { name: '交换', exact: true }).click()
  await compare.click()
  await expect(change.getByRole('cell').nth(1)).toHaveText('复审')
  await expect(change.getByRole('cell').nth(2)).toHaveText('初审')
  expect(await json(request, `/api/approval/definitions/${key}/governance`, 'admin')).toEqual(before)
  await drawer.locator('.el-drawer__close-btn').click()
  await switchUser(page, 'alice')
  await expect(comparison).toHaveCount(0)
})

test('packaged definition download imports as a draft and recovers a lost response with the original key', async ({ page, request }) => {
  const key = `package.portable.${Date.now()}`
  const target = `${key}.draft`
  await publishPortableVersions(request, key)
  const drawer = await openVersions(page, key)
  const downloadReady = page.waitForEvent('download')
  await drawer.getByRole('row').filter({ has: page.getByRole('cell', { name: 'v1', exact: true }) })
    .getByRole('button', { name: '导出', exact: true }).click()
  const download = await downloadReady
  expect(download.suggestedFilename()).toBe(`${key}-v1.approval.json`)
  const file = await readFile(await download.path())
  const exported = JSON.parse(file.toString('utf8'))
  expect(exported.source).toMatchObject({ key, version: 1 })
  expect(exported.definition.nodes.review.name).toBe('初审')
  await drawer.locator('.el-drawer__close-btn').click()
  await page.getByRole('button', { name: '导入流程定义', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '导入流程定义', exact: true })
  const input = dialog.locator('input[type=file]')
  const missingResolver = JSON.parse(file.toString('utf8'))
  missingResolver.definition.nodes.review.approvers[0].type = 'UnregisteredBrowserResolver'
  await input.setInputFiles({ name: 'missing-resolver.approval.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(missingResolver)) })
  await dialog.getByRole('textbox').fill(target)
  await dialog.getByRole('button', { name: '检查文件和宿主依赖' }).click()
  await expect(dialog.getByText(/UnregisteredBrowserResolver/)).toBeVisible()
  await expect(dialog.getByRole('button', { name: '导入为草稿' })).toBeDisabled()
  await input.setInputFiles({ name: download.suggestedFilename(), mimeType: 'application/json', buffer: file })
  await expect(dialog.getByText(`来源 ${key}（${key} · v1）`)).toBeVisible()
  const preview = page.waitForResponse(r => new URL(r.url()).pathname.endsWith(`/${target}/import/validate`))
  await dialog.getByRole('button', { name: '检查文件和宿主依赖' }).click()
  const validated = await preview
  expect(validated.status()).toBe(200)
  expect(validated.request().headers()).not.toHaveProperty('idempotency-key')
  await expect(dialog.getByText('预检通过，可以创建未发布草稿')).toBeVisible()

  const submissions: { key: string | undefined; payload: string | null }[] = []
  await page.route(`**/definitions/${target}/import`, async route => {
    submissions.push({ key: route.request().headers()['idempotency-key'], payload: route.request().postData() })
    if (submissions.length === 1) {
      const committed = await route.fetch()
      expect(committed.status(), await committed.text()).toBe(200)
      await route.abort('failed')
    } else await route.continue()
  })
  await dialog.getByRole('button', { name: '导入为草稿' }).click()
  await dialog.getByRole('button', { name: '按原内容重试上次操作' }).click()
  await expect(page).toHaveURL(new RegExp(`/designer/${target.replaceAll('.', '\\.')}$`))
  expect(submissions).toHaveLength(2)
  expect(submissions[0]!.key).toBeTruthy()
  expect(submissions[1]).toEqual(submissions[0])
  const governance = await json(request, `/api/approval/definitions/${target}/governance`, 'admin')
  expect(governance).toMatchObject({ hasDraftChanges: true, lastVersion: 0, activeDefinitionId: null })
  expect(JSON.parse(governance.draftJson)).toEqual(exported.definition)
  const audit = await json(request, `/api/approval/definitions/${target}/audit`, 'admin')
  expect(audit.items).toHaveLength(1)
})

test('definition import ignores out-of-order preflights after changing the target and reopening', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  await page.goto('/definitions')
  const open = page.getByRole('button', { name: '导入流程定义', exact: true })
  await open.click()
  const dialog = page.getByRole('dialog', { name: '导入流程定义', exact: true })
  const check = dialog.getByRole('button', { name: '检查文件和宿主依赖' })
  const submit = dialog.getByRole('button', { name: '导入为草稿' })
  const target = dialog.getByRole('textbox')
  const success = dialog.getByText('预检通过，可以创建未发布草稿')
  const source = { key: 'source', name: '原流程', version: 1, publishedAt: '2026-01-01T00:00:00Z' }
  const file = { name: 'source.approval.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify({
    packageVersion: 1, kind: 'approval-definition', source,
  })) }
  const old = Promise.withResolvers<void>()
  const closed = Promise.withResolvers<void>()
  await page.route('**/definitions/*/import/validate', async route => {
    const key = new URL(route.request().url()).pathname.split('/').at(-3)
    if (key === 'old') await old.promise
    if (key === 'closed') await closed.promise
    if (key === 'occupied') {
      await route.fulfill({ status: 409, json: { code: 'Conflict', title: '目标标识已占用' } })
    } else await route.fulfill({ json: source })
  })

  async function preview(key: string) {
    await target.fill(key)
    const started = page.waitForRequest(`**/definitions/${key}/import/validate`)
    await check.click()
    await started
  }
  async function finish(key: string, release: () => void) {
    const completed = page.waitForResponse(`**/definitions/${key}/import/validate`)
    release()
    expect(await (await completed).finished()).toBeNull()
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))))
  }

  try {
    await dialog.locator('input[type=file]').setInputFiles(file)
    await preview('old')
    await preview('occupied')
    await expect(dialog.getByText('目标标识已占用')).toBeVisible()
    await finish('old', old.resolve)
    await expect(target).toHaveValue('occupied')
    await expect(submit).toBeDisabled()

    await preview('closed')
    await dialog.locator('.el-dialog__headerbtn').click()
    await open.click()
    await dialog.locator('input[type=file]').setInputFiles(file)
    await target.fill('unvalidated')
    await finish('closed', closed.resolve)
    await expect(success).toHaveCount(0)
    await expect(submit).toBeDisabled()
  } finally {
    old.resolve()
    closed.resolve()
  }
})

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

test('packaged simulation stops at decisions and node preview leaves the saved draft unchanged', async ({ page, request }) => {
  const api = process.env.APPROVAL_SMOKE_API!, key = `package.simulate.${Date.now()}`
  const flow = { schemaVersion: 1, startNodeId: 's', nodes: {
    s: { type: 'Start', name: '提交', next: 'c' },
    c: { type: 'Condition', name: '金额判断', branches: [{ priority: 1, expression: 'amount >= 5000', next: 'high' }, { priority: 2, next: 'low' }] },
    high: { type: 'Approval', name: '高金额审批', approvers: [{ type: 'User', value: 'dave' }], next: 'e' },
    low: { type: 'Approval', name: '普通审批', approvers: [{ type: 'User', value: 'quinn' }], next: 'e' }, e: { type: 'End', name: '结束' },
  } }
  const published = await request.post(api + `/api/approval/definitions/${key}`, { headers: { 'X-User-Id': 'admin', 'Idempotency-Key': key }, data: flow })
  expect(published.ok(), await published.text()).toBe(true)
  const before = await json(request, `/api/approval/definitions/${key}/governance`, 'admin')
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  await page.goto(`/designer/${key}`)
  await page.getByRole('button', { name: '流程模拟', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '流程模拟', exact: true })
  await dialog.getByRole('textbox').fill('{"amount":8000}')
  const simulation = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/definitions/simulate'))
  await dialog.getByRole('button', { name: '执行模拟', exact: true }).click()
  const response = await simulation
  expect(response.status()).toBe(200)
  expect(response.request().headers()).not.toHaveProperty('idempotency-key')
  expect((await response.json()).isComplete).toBe(false)
  await expect(dialog.getByText('需要人工决策；后续路径未模拟', { exact: true })).toBeVisible()
  await expect(dialog.getByText('任务 / 抄送对象：User · dave', { exact: true })).toBeVisible()
  await dialog.getByRole('textbox').fill('{"amount":1}')
  await expect(dialog.getByText('任务 / 抄送对象：User · dave', { exact: true })).toHaveCount(0)
  await dialog.getByRole('button', { name: '执行模拟', exact: true }).click()
  await expect(dialog.getByText('任务 / 抄送对象：User · quinn', { exact: true })).toBeVisible()
  await dialog.locator('.el-dialog__headerbtn').click()
  await expect(dialog).not.toBeVisible()
  await page.locator('.vue-flow__node.selectable[data-id="high"]').click()
  await page.getByRole('button', { name: '预览节点分配', exact: true }).click()
  const preview = page.getByRole('dialog', { name: '审批人解析预览', exact: true })
  await preview.getByRole('button', { name: '执行模拟', exact: true }).click()
  await expect(preview.getByText('任务 / 抄送对象：User · dave', { exact: true })).toBeVisible()
  const after = await json(request, `/api/approval/definitions/${key}/governance`, 'admin')
  expect(after.revision).toBe(before.revision); expect(after.draftJson).toBe(before.draftJson)
  expect(after.activeDefinitionId).toBe(before.activeDefinitionId)
})

test('packaged designer inserts an Action node and publishes calculation, SQL, HTTP and notification steps', async ({ page, request }) => {
  const api = process.env.APPROVAL_SMOKE_API!, key = `package.actions.${Date.now()}`
  const flow = { schemaVersion: 1, startNodeId: 's', settings: { startable: true },
    form: { fields: [{ key: 'product', label: '商品', type: 'Text' }] }, nodes: {
      s: { type: 'Start', name: '提交', next: 'e', position: { x: 0, y: 0 } },
      e: { type: 'End', name: '结束', position: { x: 150, y: 260 } },
    } }
  const seed = await request.post(api + `/api/approval/definitions/${key}`, {
    headers: { 'X-User-Id': 'admin', 'Idempotency-Key': key }, data: flow,
  })
  expect(seed.ok(), await seed.text()).toBe(true)
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'admin'))
  await page.goto(`/designer/${key}`)
  const edge = page.locator('.vue-flow__edge-path').first()
  await expect(edge).toBeVisible()
  const midpoint = await edge.evaluate(path => {
    const svgPath = path as SVGPathElement
    const point = svgPath.getPointAtLength(svgPath.getTotalLength() / 2).matrixTransform(svgPath.getScreenCTM()!)
    return { clientX: point.x, clientY: point.y }
  })
  const transfer = await page.evaluateHandle(() => {
    const data = new DataTransfer()
    data.setData('application/x-approval-node', 'Action')
    return data
  })
  await page.locator('.vue-flow').dispatchEvent('drop', { ...midpoint, dataTransfer: transfer })
  const editor = page.locator('.actions-editor')
  await expect(editor).toBeVisible()
  const config = editor.locator('.actions-editor__config')
  async function add(label: string) {
    const button = editor.getByRole('button', { name: '添加动作', exact: true })
    await expect(button).toBeEnabled()
    await button.click()
    await config.locator('.el-select').first().click()
    await page.getByRole('option', { name: label, exact: true }).click()
  }
  async function choose(label: string, value: string) {
    const field = config.locator('.el-form-item').filter({ has: page.getByText(label, { exact: true }) })
    await field.locator('.el-select').click()
    await page.getByRole('option', { name: value, exact: true }).click()
  }
  await add('JavaScript')
  await config.getByLabel('JavaScript', { exact: true }).fill('function execute(c) { return { product: c.form.product }; }')
  await config.getByPlaceholder('如 risk').fill('risk')
  await add('SQL 查询')
  await choose('数据源', 'warehouse')
  await choose('查询 ID', 'inventory.available')
  await config.getByRole('textbox', { name: '查询参数' }).fill('{"productCode":"{{outputs.risk.product}}"}')
  await config.getByPlaceholder('如 risk').fill('stock')
  await add('数据映射')
  await config.getByRole('button', { name: '添加目标字段' }).click()
  await config.getByLabel('源路径').fill('outputs.stock.rows[0].quantity')
  await config.getByLabel('目标路径').fill('quantity')
  await config.getByPlaceholder('如 risk').fill('erpPayload')
  await add('HTTP 请求')
  await choose('执行模式', '提交后投递')
  await choose('宿主目标', 'erp')
  await choose('HTTP 方法', 'POST')
  await config.getByRole('textbox', { name: 'JSON 请求体' }).fill('{"inventory":"{{outputs.erpPayload}}"}')
  await add('消息通知')
  await config.locator('.el-form-item').filter({ has: page.getByText('消息标题', { exact: true }) })
    .getByRole('textbox').fill('动作验收完成')
  await config.getByPlaceholder('例如：订单 {{business.key}} 已审批').fill('库存 {{outputs.erpPayload.quantity}}')
  const published = page.waitForResponse(r => new URL(r.url()).pathname.endsWith(`/${key}/publish-draft`))
  await page.getByRole('button', { name: '发布新版本', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '确定', exact: true }).click()
  expect((await published).status()).toBe(200)
  const start = await request.post(api + `/api/approval/forms/${key}/start`, {
    headers: { 'X-User-Id': 'alice', 'Idempotency-Key': `${key}.start` }, data: { formData: { product: 'P001' } },
  })
  expect(start.ok(), await start.text()).toBe(true)
  const { instanceId } = await start.json()
  await page.goto(`/approvals/${instanceId}`)
  await expect(page.getByText('动作执行记录', { exact: true })).toBeVisible()
  await expect(page.getByText(/已入队 ·/)).toBeVisible()
  await page.getByRole('link', { name: '查看此实例的 Outbox' }).click()
  const delivery = page.getByRole('row').filter({ hasText: 'Http/step4' })
  await expect(delivery).toBeVisible()
  const retried = page.waitForResponse(r => /\/api\/approval\/outbox\/[^/]+\/retry$/.test(new URL(r.url()).pathname))
  await delivery.getByRole('button', { name: '重试', exact: true }).click()
  expect((await retried).status()).toBe(204)
  await expect(page.getByText('已安排重新投递')).toBeVisible()
})

test('packaged purchase example prepares stock and queues ERP only after approval', async ({ request }) => {
  const key = `package.purchase.actions.${Date.now()}`
  const api = process.env.APPROVAL_SMOKE_API!
  const definition = await readFile('../samples/Certificate.Api/ActionExamples/purchase-action.json', 'utf8')
  const published = await request.post(`${api}/api/approval/definitions/${key}`, {
    headers: { 'X-User-Id': 'admin', 'Idempotency-Key': key, 'Content-Type': 'application/json' }, data: definition,
  })
  expect(published.ok(), await published.text()).toBe(true)
  const started = await request.post(`${api}/api/approval/forms/${key}/start`, {
    headers: { 'X-User-Id': 'alice', 'Idempotency-Key': `${key}.start` }, data: { formData: { productCode: 'P001', amount: 58000 } },
  })
  expect(started.ok(), await started.text()).toBe(true)
  const { instanceId } = await started.json()
  const outbox = () => json(request, `/api/approval/outbox?instanceId=${instanceId}`, 'admin')
  expect((await outbox()).items.some((item: { target: string }) => item.target === 'WorkflowAction')).toBe(false)
  const pending = await json(request, `/api/approval/instances/${instanceId}/my-task`, 'dave')
  const approved = await request.post(`${api}/api/approval/tasks/${pending.task.id}/approve`, {
    headers: { 'X-User-Id': 'dave', 'Idempotency-Key': `${key}.approve` }, data: {},
  })
  expect(approved.status(), await approved.text()).toBe(204)
  const queued = (await outbox()).items.filter((item: { target: string }) => item.target === 'WorkflowAction')
  expect(queued).toHaveLength(1)
  expect(queued[0]).toMatchObject({ nodeId: 'deliver', description: 'Http/erp', status: 'Pending' })
  expect(queued[0]).not.toHaveProperty('payloadJson')
  const messages = await json(request, '/api/approval/messages', 'alice')
  expect(messages).toEqual(expect.arrayContaining([expect.objectContaining({
    instanceId, title: '采购审批已完成', content: 'P001 · 金额 58000 · 风险 high',
  })]))
})

test('packaged approval recovers the original key and payload after the server commits but response is lost', async ({ page, request }) => {
  const api = process.env.APPROVAL_SMOKE_API!, key = `package.recovery.${Date.now()}`
  const flow = { schemaVersion: 1, startNodeId: 's', settings: { startable: true },
    form: { fields: [{ key: 'memo', label: '说明', type: 'Text' }] }, nodes: {
      s: { type: 'Start', name: '提交', next: 'review' },
      review: { type: 'Approval', name: '审核', approvers: [{ type: 'User', value: 'dave' }], next: 'e' }, e: { type: 'End', name: '结束' },
    } }
  const published = await request.post(api + `/api/approval/definitions/${key}`, { headers: { 'X-User-Id': 'admin', 'Idempotency-Key': key }, data: flow })
  expect(published.ok(), await published.text()).toBe(true)
  const started = await request.post(api + `/api/approval/forms/${key}/start`, { headers: { 'X-User-Id': 'alice', 'Idempotency-Key': `${key}.start` }, data: { formData: { memo: 'original' }, comment: '' } })
  expect(started.ok(), await started.text()).toBe(true)
  const { instanceId } = await started.json()
  await page.addInitScript(() => localStorage.setItem('approval-demo-user', 'dave'))
  await page.goto(`/approvals/${instanceId}`)
  const submissions: { key: string | undefined; payload: string | null }[] = []
  await page.route('**/tasks/*/approve', async route => {
    submissions.push({ key: route.request().headers()['idempotency-key'], payload: route.request().postData() })
    if (submissions.length === 1) {
      const committed = await route.fetch()
      expect(committed.status()).toBe(204)
      await route.abort('failed')
    } else await route.continue()
  })
  await page.getByRole('button', { name: '同意', exact: true }).click()
  await page.getByRole('dialog').getByRole('textbox').fill('original-comment')
  await page.getByRole('dialog').getByRole('button', { name: '确定', exact: true }).click()
  const retry = page.getByRole('button', { name: '按原内容重试上次操作', exact: true })
  // Same-page recovery retains the original payload in memory; this case does not reload the document.
  await expect(retry).toBeVisible()
  await expect(page.getByRole('button', { name: '同意', exact: true })).toHaveCount(0)
  await retry.click()
  await expect(retry).toHaveCount(0)
  expect(submissions).toHaveLength(2)
  expect(submissions[0]!.key).toBeTruthy()
  expect(submissions[1]).toEqual(submissions[0])
  const detail = await json(request, `/api/approval/instances/${instanceId}`, 'admin')
  expect(detail.status).toBe('Approved')
  expect(detail.logs.filter((log: { action: string }) => log.action === 'Approve')).toHaveLength(1)
})
