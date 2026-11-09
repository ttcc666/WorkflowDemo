import { expect, test } from '@playwright/test'

test('a packed third-party host mounts independent pages with its own router extensions identity and scoped CSS and completes an approval', async ({ page, request }) => {
  const key = 'standalone-' + crypto.randomUUID()
  const api = process.env.APPROVAL_SMOKE_API! + '/api/approval'
  const definition = await request.post(`${api}/definitions/${key}?name=${key}`, { headers: { 'X-User-Id': 'admin', 'Idempotency-Key': crypto.randomUUID() }, data: {
    schemaVersion: 1, startNodeId: 'start', settings: { startable: true, titleTemplate: '{form.subject}' }, form: { fields: [
      { key: 'subject', label: '宿主申请主题', type: 'Text', required: true },
      { key: 'person', label: '宿主联系人', type: 'User' },
    ] }, nodes: {
      start: { id: 'start', name: '开始', type: 'Start', next: 'review' },
      review: { id: 'review', name: '宿主审批', type: 'Approval', mode: 'Any', approvers: [{ type: 'User', value: 'bob' }], next: 'end' },
      end: { id: 'end', name: '结束', type: 'End' },
    },
  } })
  expect(definition.ok(), await definition.text()).toBe(true)
  await page.goto(`/host.html#/embedded/start/${key}`)
  await page.getByRole('textbox', { name: '宿主申请主题' }).fill('独立包真实申请')
  await page.getByRole('group', { name: '宿主联系人', exact: true }).getByRole('combobox').selectOption('bob')
  expect(await page.getByTestId('host-header').evaluate(element => ({ color: getComputedStyle(element).color, family: getComputedStyle(element).fontFamily }))).toEqual({ color: 'rgb(20, 40, 60)', family: 'serif' })
  expect(await page.locator('.approval-react').evaluate(element => getComputedStyle(element).getPropertyValue('--primary'))).not.toBe('')
  const started = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith(`/forms/${key}/start`))
  await page.getByRole('button', { name: '提交申请', exact: true }).click()
  const response = await started
  expect(response.ok()).toBe(true)
  const id = (await response.json()).instanceId as string
  await expect(page).toHaveURL(new RegExp(`#\/embedded\/approvals\/${id}$`))
  await expect(page.getByTestId('host-business')).toContainText('宿主业务：')
  await page.getByRole('combobox', { name: /^宿主身份/ }).selectOption('bob')
  await page.getByRole('button', { name: '同意', exact: true }).click()
  const approved = page.waitForResponse(result => result.request().method() === 'POST' && /\/tasks\/[^/]+\/approve$/.test(new URL(result.url()).pathname))
  await page.getByRole('button', { name: '确认同意', exact: true }).click()
  expect((await approved).ok()).toBe(true)
  const final = await request.get(`${api}/instances/${id}`, { headers: { 'X-User-Id': 'admin' } })
  expect(final.ok()).toBe(true)
  expect((await final.json()).status).toBe('Approved')
  await page.getByRole('link', { name: '返回审批工作台', exact: true }).click()
  await expect(page).toHaveURL(/host\.html#\/embedded\/inbox$/)
  await page.getByRole('link', { name: '宿主其他页面' }).click()
  await expect(page.getByText('宿主自己的页面')).toBeVisible()
  await expect(page).toHaveURL(/host\.html#\/other$/)
})
