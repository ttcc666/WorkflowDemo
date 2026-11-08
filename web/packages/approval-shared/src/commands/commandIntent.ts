import { createStateCell } from '../state.js'
import { ApiError } from '../api/http.js'

interface Intent { key: string; fingerprint: string; uncertain: boolean }
type IntentStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
function canonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonical)
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([key, item]) => [key, canonical(item)]))
  return value
}
const memory = new Map<string, string>()
const failedWrites = new WeakMap<IntentStorage, Map<string, string | null>>()
function defaultStorage(): IntentStorage {
  try { if (typeof window !== 'undefined') return window.sessionStorage } catch { /* Private browsing may disable storage. */ }
  return { getItem: key => memory.get(key) ?? null, setItem: (key, value) => { memory.set(key, value) }, removeItem: key => { memory.delete(key) } }
}
/** One key per logical intent. Only a digest and opaque key are kept; request bodies are never persisted. */
export function createCommandIntent(identity: () => string, options: { storage?: IntentStorage; generate?: () => string; retainOnError?: boolean } = {}) {
  const storage = options.storage ?? defaultStorage()
  const fallback = failedWrites.get(storage) ?? new Map<string, string | null>()
  failedWrites.set(storage, fallback)
  const generate = options.generate ?? (() => crypto.randomUUID())
  const pending = createStateCell<string | null>(null)
  const running = new Set<string>()
  // Sensitive payloads and bound callbacks stay in this page only, never in browser storage.
  const retries = new Map<string, () => Promise<unknown>>()
  const storageKey = (scope: string) => `approval.intent.v1:${encodeURIComponent(identity())}:${encodeURIComponent(scope)}`
  function read(key: string): Intent | undefined {
    let stored = fallback.get(key)
    if (!fallback.has(key)) try { stored = storage.getItem(key) } catch { /* Retain the current page's intent when storage is unavailable. */ }
    try { const value = JSON.parse(stored ?? 'null'); return value && typeof value.key === 'string' && typeof value.fingerprint === 'string' ? value : undefined } catch { return undefined }
  }
  function save(key: string, value?: Intent) {
    fallback.set(key, value ? JSON.stringify(value) : null)
    try {
      if (value) storage.setItem(key, JSON.stringify(value)); else storage.removeItem(key)
      fallback.delete(key)
    } catch { /* Keep failed writes, including deletions, until the page closes. */ }
  }
  async function run<P, T>(scope: string, payload: P, send: (body: P, operationId: string) => Promise<T>): Promise<T> {
    const owner = identity(); const key = storageKey(scope)
    if (running.has(key)) throw new Error('正在提交，请等待结果')
    running.add(key)
    try {
      const json = JSON.stringify(payload) ?? 'null'
      const body = JSON.parse(json) as P
      const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(canonical(body))))
      if (identity() !== owner) throw new DOMException('身份已切换', 'AbortError')
      const fingerprint = Array.from(new Uint8Array(digest), n => n.toString(16).padStart(2, '0')).join('')
      const previous = read(key)
      if (previous) pending.value = scope
      if (previous && previous.fingerprint !== fingerprint) throw new Error('上次提交结果尚未确认，请先使用原内容重试或检查已发起的记录，再开始新申请')
      const intent = previous ?? { key: generate(), fingerprint, uncertain: false }
      save(key, intent); pending.value = scope
      retries.set(key, () => run(scope, JSON.parse(json) as P, send))
      try {
        const result = await send(body, intent.key)
        if (identity() !== owner) throw new DOMException('身份已切换', 'AbortError')
        save(key); pending.value = null
        retries.delete(key)
        return result
      } catch (error) {
        if (!options.retainOnError && !intent.uncertain && error instanceof ApiError && error.status < 500 && error.code !== 'CommitOutcomeUnknown') { save(key); pending.value = null; retries.delete(key) }
        else { intent.uncertain = true; save(key, intent) }
        throw error
      }
    } finally { running.delete(key) }
  }
  function canRetry(scope = pending.value) { return scope !== null && retries.has(storageKey(scope)) }
  async function retry(scope = pending.value): Promise<unknown> {
    const replay = scope === null ? undefined : retries.get(storageKey(scope))
    if (!replay) throw new Error('当前页面没有原请求内容，请核对记录后恢复原内容')
    return replay()
  }
  return { run, pending, retry, canRetry, hasPending: (scope: string) => !!read(storageKey(scope)), reset: (scope: string) => { const key = storageKey(scope); save(key); retries.delete(key); pending.value = null } }
}
export type CommandIntent = ReturnType<typeof createCommandIntent>
