import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { expect, it } from 'vitest'
import { ApprovalProvider, useApprovalUser } from '../src/client'

it('keeps two owned Providers independent through StrictMode and identity changes with Forms disabled', async () => {
  const requested: string[] = []
  function Panel({ label }: { label: string }) {
    const user = useApprovalUser()
    return <output aria-label={label}>{user.userId}:{String(user.formsEnabled)}:{String(user.can('approval.definition.publish'))}</output>
  }
  function App({ owner }: { owner: string }) {
    const options = (id: string) => ({ userKey: () => id, headers: () => ({ 'X-User-Id': id }), fetcher: async (_url: string, init?: RequestInit) => {
      const supplied = new Headers(init?.headers).get('X-User-Id')!
      requested.push(supplied)
      return Response.json({ userId: supplied, formsEnabled: false, capabilities: {} })
    } })
    return <StrictMode>
      <ApprovalProvider options={options(owner)}><Panel label="first" /></ApprovalProvider>
      <ApprovalProvider options={options('bob')}><Panel label="second" /></ApprovalProvider>
    </StrictMode>
  }
  const view = render(<App owner="alice" />)
  await waitFor(() => expect(screen.getByLabelText('first')).toHaveTextContent('alice:false:false'))
  expect(screen.getByLabelText('second')).toHaveTextContent('bob:false:false')
  view.rerender(<App owner="carol" />)
  await waitFor(() => expect(screen.getByLabelText('first')).toHaveTextContent('carol:false:false'))
  expect(screen.getByLabelText('second')).toHaveTextContent('bob:false:false')
  expect(new Set(requested)).toEqual(new Set(['alice', 'bob', 'carol']))
})
