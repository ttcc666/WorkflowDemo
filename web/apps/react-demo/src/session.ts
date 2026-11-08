export function createDemoSession(storage: Pick<Storage, 'getItem' | 'setItem'>) {
  let user = 'alice'
  try { user = storage.getItem('approval-demo-user') || user } catch { /* Demo remains usable when storage is unavailable. */ }
  const listeners = new Set<() => void>()
  return {
    getSnapshot: () => user,
    headers: () => ({ 'X-User-Id': user }),
    switchUser(next: string) {
      if (!next || next === user) return
      user = next
      try { storage.setItem('approval-demo-user', user) } catch { /* Do not lose current session identity. */ }
      for (const notify of listeners) notify()
    },
    subscribe: (notify: () => void) => { listeners.add(notify); return () => { listeners.delete(notify) } },
  }
}
