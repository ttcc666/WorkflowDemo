import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { expect, it } from 'vitest'
import { ApprovalProvider } from '../../src/client'
import DefinitionsPage from '../../src/pages/DefinitionsPage'
import StartFormPage from '../../src/pages/StartFormPage'

function show(page: 'copy' | 'start') {
  let release!: (value: Response) => void
  const posts: { url: string; key: string | null; body: unknown }[] = []
  const fetcher = async (url: string, init: RequestInit) => {
    if (init.method === 'POST') {
      posts.push({ url, key: new Headers(init.headers).get('Idempotency-Key'), body: JSON.parse(String(init.body)) })
      return new Promise<Response>(resolve => { release = resolve })
    }
    if (url.endsWith('/me')) return Response.json({ userId: 'admin', canViewDefinitions: true, formsEnabled: true,
      capabilities: { 'approval.definition.design': true, 'approval.instance.start': true } })
    if (url.includes('/definitions/governance')) return Response.json([{ key: 'original', name: '原流程', lastVersion: 1, activeDefinitionId: 'published', isEnabled: true }])
    if (url.endsWith('/definitions/categories')) return Response.json([])
    return Response.json({ key: 'flow', name: '真实申请页面', form: { fields: [{ key: 'subject', label: '申请主题', type: 'Text', required: true, defaultValue: '当前申请' }] }, settings: {} })
  }
  const router = createMemoryRouter([
    { path: '/definitions', Component: DefinitionsPage }, { path: '/start/:key', Component: StartFormPage },
    { path: '/other', element: <p>宿主正在处理的其他页面</p> },
    { path: '/designer/:key', element: <p>不应跳回的设计器</p> }, { path: '/approvals/:id', element: <p>不应跳回的详情</p> },
  ], { initialEntries: [page === 'copy' ? '/definitions' : '/start/flow'] })
  render(<ApprovalProvider options={{ fetcher }}><RouterProvider router={router} /></ApprovalProvider>)
  return { router, posts, complete: (value: unknown) => act(async () => release(Response.json(value))) }
}
it('a late definition copy result cannot redirect the host after leaving the page', async () => {
  const view = show('copy')
  fireEvent.click(await screen.findByRole('button', { name: '复制' }))
  fireEvent.click(screen.getByRole('button', { name: '确认复制' }))
  await waitFor(() => expect(view.posts).toHaveLength(1))
  expect(view.posts[0]?.url).toContain('/definitions/original/copy')
  expect(view.posts[0]?.body).toEqual({ targetKey: 'original-copy', name: '原流程（副本）', sourceDefinitionId: 'published' })
  expect(view.posts[0]?.key).toBeTruthy()
  await act(async () => { await view.router.navigate('/other') })
  await view.complete({ key: 'copied' })
  expect(view.router.state.location.pathname).toBe('/other')
  expect(screen.getByText('宿主正在处理的其他页面')).toBeVisible()
  expect(view.posts).toHaveLength(1)
})
it('a late form submission result cannot redirect the host after leaving the page', async () => {
  const view = show('start')
  await waitFor(() => expect(screen.getByRole('textbox', { name: '申请主题' })).toHaveValue('当前申请'))
  fireEvent.click(screen.getByRole('button', { name: '提交申请' }))
  await waitFor(() => expect(view.posts).toHaveLength(1))
  expect(view.posts[0]?.url).toContain('/forms/flow/start')
  expect(view.posts[0]?.body).toEqual({ formData: { subject: '当前申请' }, comment: '', draftId: null })
  expect(view.posts[0]?.key).toBeTruthy()
  await act(async () => { await view.router.navigate('/other') })
  await view.complete({ instanceId: 'completed' })
  expect(view.router.state.location.pathname).toBe('/other')
  expect(screen.getByText('宿主正在处理的其他页面')).toBeVisible()
  expect(view.posts).toHaveLength(1)
})
