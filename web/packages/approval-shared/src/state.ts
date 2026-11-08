/** Small framework-neutral observable cell; snapshots keep their identity until changed. */
export function createStateCell<T>(initial: T) {
  let value = initial
  const listeners = new Set<() => void>()
  return {
    get value() { return value },
    set value(next: T) { if (Object.is(next, value)) return; value = next; for (const notify of listeners) notify() },
    getSnapshot: () => value,
    subscribe: (notify: () => void) => { listeners.add(notify); return () => { listeners.delete(notify) } },
  }
}
