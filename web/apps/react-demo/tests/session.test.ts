import { expect, it, vi } from 'vitest'
import { createDemoSession } from '../src/session'
it('switches the demo identity and request header together even when browser storage is disabled', () => {
  const storage = { getItem: () => 'bob', setItem: vi.fn(() => { throw new Error('disabled') }) }
  const session = createDemoSession(storage)
  const changed = vi.fn(); const unsubscribe = session.subscribe(changed)
  expect(session.headers()).toEqual({ 'X-User-Id': 'bob' })
  session.switchUser('alice')
  expect(session.getSnapshot()).toBe('alice')
  expect(session.headers()).toEqual({ 'X-User-Id': 'alice' })
  expect(storage.setItem).toHaveBeenCalledWith('approval-demo-user', 'alice')
  expect(changed).toHaveBeenCalledOnce()
  unsubscribe(); session.switchUser('admin'); expect(changed).toHaveBeenCalledOnce()
})
