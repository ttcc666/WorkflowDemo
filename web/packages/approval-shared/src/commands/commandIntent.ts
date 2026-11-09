import { createStateCell } from '../state.js'
import { ApiError } from '../api/http.js'

interface Intent { key: string; fingerprint: string; uncertain: boolean }
const legacyNamespaces: Record<string, string> = {
  'user-reassign': 'bulk-reassign',
  'cc-read': 'carbon-copy-read',
  'form-start': 'start-form',
  'message-read': 'message',
  'definition-import': 'import-definition',
  'definition-copy': 'copy-definition',
}
/** Both UIs use the same identity; legacy records remain readable until their result is reviewed. */
export function resolveCommandNamespace(value: string) {
  const namespace = legacyNamespaces[value] ?? value
  return { namespace, aliases: Object.keys(legacyNamespaces).filter(key => legacyNamespaces[key] === namespace) }
}
export function commandScopeAliases(namespace: string, scope: string): string[] {
  // Vue used the reject scope for a return. The retained body still determines the original operation.
  if (namespace === 'approval-detail' && scope.startsWith('return:')) return ['reject:' + scope.slice(7)]
  if (namespace === 'approval-detail' && scope.startsWith('reject:')) return ['return:' + scope.slice(7)]
  return []
}
type IntentStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> & Partial<Pick<Storage, 'length' | 'key'>>
function canonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonical)
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([key, item]) => [key, canonical(item)]))
  return value
}
const memory = new Map<string, string>()
const memoryStorage: IntentStorage = { get length() { return memory.size }, key: index => [...memory.keys()][index] ?? null, getItem: key => memory.get(key) ?? null, setItem: (key, value) => { memory.set(key, value) }, removeItem: key => { memory.delete(key) } }
const failedWrites = new WeakMap<IntentStorage, Map<string, string | null>>()
const storageChanges = new WeakMap<IntentStorage, { revision: ReturnType<typeof createStateCell<number>>; keys: Set<string> }>()
function defaultStorage(): IntentStorage {
  try { if (typeof window !== 'undefined') return window.sessionStorage } catch { /* Private browsing may disable storage. */ }
  return memoryStorage
}
/** One key per logical intent. Only a digest and opaque key are kept; request bodies are never persisted. */
export function createCommandIntent(identity: () => string, options: { storage?: IntentStorage; generate?: () => string; retainOnError?: boolean; aliases?: () => readonly string[]; scopeAliases?: (scope: string) => readonly string[]; canExecute?: (scope: string) => boolean } = {}) {
  const storage = options.storage ?? defaultStorage()
  const changes = storageChanges.get(storage) ?? { revision: createStateCell(0), keys: new Set<string>() }
  storageChanges.set(storage, changes)
  const notify = () => { changes.revision.value++ }
  const fallback = failedWrites.get(storage) ?? new Map<string, string | null>()
  failedWrites.set(storage, fallback)
  const generate = options.generate ?? (() => crypto.randomUUID())
  const pending = createStateCell<string | null>(null)
  const running = new Set<string>()
  // Sensitive payloads and bound callbacks stay in this page only, never in browser storage.
  const retries = new Map<string, () => Promise<unknown>>()
  const storagePrefix = (owner: string) => `approval.intent.v1:${encodeURIComponent(owner)}:`
  const storageKey = (scope: string) => `${storagePrefix(identity())}${encodeURIComponent(scope)}`
  const storageKeys = (scope: string) => [...new Set([identity(), ...options.aliases?.() ?? []])].flatMap(owner => [scope, ...options.scopeAliases?.(scope) ?? []].map(value => `${storagePrefix(owner)}${encodeURIComponent(value)}`))
  function read(key: string): Intent | undefined {
    let stored = fallback.get(key)
    if (!fallback.has(key)) try { stored = storage.getItem(key) } catch { /* Retain the current page's intent when storage is unavailable. */ }
    try { const value = JSON.parse(stored ?? 'null'); return value && typeof value.key === 'string' && typeof value.fingerprint === 'string' ? value : undefined } catch { return undefined }
  }
  function save(key: string, value?: Intent) {
    if (value) changes.keys.add(key); else changes.keys.delete(key)
    fallback.set(key, value ? JSON.stringify(value) : null)
    try {
      if (value) storage.setItem(key, JSON.stringify(value)); else storage.removeItem(key)
      fallback.delete(key)
    } catch { /* Keep failed writes, including deletions, until the page closes. */ }
  }
  function records(scope: string) { return storageKeys(scope).map(key => ({ key, intent: read(key) })).filter(record => record.intent !== undefined) }
  function previousIntent(scope: string) {
    const values = records(scope).map(record => record.intent!)
    if (values.some(value => value.key !== values[0]?.key || value.fingerprint !== values[0]?.fingerprint))
      throw new Error('检测到多笔未确认操作，请人工核对全部结果后再开始新操作')
    return values[0] ? { ...values[0], uncertain: values.some(value => value.uncertain) } : undefined
  }
  function clear(scope: string) { for (const key of storageKeys(scope)) save(key) }
  function clearPending(scope: string) { if (pending.value !== null && [scope, ...options.scopeAliases?.(scope) ?? []].includes(pending.value)) pending.value = pendingScopes()[0] ?? null }
  function pendingScopes() {
    const keys = new Set([...changes.keys, ...fallback.keys()])
    try { for (let index = 0; index < (storage.length ?? 0); index++) { const key = storage.key?.(index); if (key) keys.add(key) } } catch { /* Use page memory when browser storage is inaccessible. */ }
    const prefixes = [identity(), ...options.aliases?.() ?? []].map(storagePrefix)
    const scopes = new Set<string>()
    for (const key of keys) {
      const prefix = prefixes.find(value => key.startsWith(value))
      if (prefix && read(key)) try { scopes.add(decodeURIComponent(key.slice(prefix.length))) } catch { /* Ignore malformed storage keys. */ }
    }
    return [...scopes].sort()
  }
  async function run<P, T>(scope: string, payload: P, send: (body: P, operationId: string) => Promise<T>): Promise<T> {
    if (options.canExecute && !options.canExecute(scope)) throw new ApiError('没有执行此操作的权限', 403)
    const owner = identity(); const key = storageKey(scope)
    if (running.has(key)) throw new Error('正在提交，请等待结果')
    running.add(key)
    try {
      const json = JSON.stringify(payload) ?? 'null'
      const body = JSON.parse(json) as P
      const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(canonical(body))))
      if (identity() !== owner) throw new DOMException('身份已切换', 'AbortError')
      const fingerprint = Array.from(new Uint8Array(digest), n => n.toString(16).padStart(2, '0')).join('')
      if (records(scope).length) pending.value = scope
      const previous = previousIntent(scope)
      if (previous && previous.fingerprint !== fingerprint) throw new Error('上次提交结果尚未确认，请先使用原内容重试或检查已发起的记录，再开始新申请')
      if (options.canExecute && !options.canExecute(scope)) throw new ApiError('没有执行此操作的权限', 403)
      const intent = previous ?? { key: generate(), fingerprint, uncertain: false }
      save(key, intent); pending.value = scope
      retries.set(key, () => run(scope, JSON.parse(json) as P, send))
      notify()
      try {
        const result = await send(body, intent.key)
        if (identity() !== owner) throw new DOMException('身份已切换', 'AbortError')
        clear(scope); clearPending(scope)
        retries.delete(key)
        notify()
        return result
      } catch (error) {
        if (!options.retainOnError && !intent.uncertain && error instanceof ApiError && error.status < 500 && error.code !== 'CommitOutcomeUnknown') { clear(scope); clearPending(scope); retries.delete(key) }
        else { intent.uncertain = true; save(key, intent) }
        notify()
        throw error
      }
    } finally { running.delete(key) }
  }
  function canRetry(scope = pending.value) { if (scope === null || !retries.has(storageKey(scope))) return false; try { previousIntent(scope); return true } catch { return false } }
  async function retry(scope = pending.value): Promise<unknown> {
    const replay = scope === null ? undefined : retries.get(storageKey(scope))
    if (!replay) throw new Error('当前页面没有原请求内容，请核对记录后恢复原内容')
    return replay()
  }
  return { run, pending, revision: changes.revision, pendingScopes, retry, canRetry, hasConflictingPending: (scope: string) => { try { previousIntent(scope); return false } catch { return true } }, hasPending: (scope: string) => records(scope).length > 0, reset: (scope: string) => { clear(scope); retries.delete(storageKey(scope)); clearPending(scope); notify() } }
}
export type CommandIntent = ReturnType<typeof createCommandIntent>
