import {
  createContext,
  useContext,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
  type ComponentType,
  type ReactNode,
} from 'react'
import {
  createApprovalClientCore,
  type ApprovalClientCore,
} from '@approval/shared/client'
import type { ApprovalTransportOptions } from '@approval/shared'
import { resolveTransportOptions } from '@approval/shared/transportOptions'
import { TooltipProvider } from './components/shadcn/tooltip'

export interface ApprovalUIOptions extends ApprovalTransportOptions {
  businessPanels?: Record<string, ComponentType<{ businessKey: string }>>
  outboxPanel?: ComponentType
  userPicker?: ComponentType<{
    value: string[]
    onChange: (ids: string[]) => void
    multiple?: boolean
    excludedUsers?: string[]
    disabled?: boolean
    label?: string
  }>
}
export type ApprovalClient = ApprovalClientCore & {
  options: () => ApprovalUIOptions
}
export function createApprovalClient(
  initial: ApprovalUIOptions = {},
): ApprovalClient {
  let options = initial
  const core = createApprovalClientCore(options)
  const update = core.updateOptions
  return Object.assign(core, {
    options: () => options,
    updateOptions(next: ApprovalUIOptions) {
      options = { ...options, ...next }
      update(options)
    },
  })
}
const ClientContext = createContext<ApprovalClient | null>(null)
const IdentityChangeContext = createContext(false)
export function useApprovalIdentityChange() { return useContext(IdentityChangeContext) }
export function ApprovalProvider({
  options = {},
  client: supplied,
  children,
}: {
  options?: ApprovalUIOptions
  client?: ApprovalClient
  children: ReactNode
}) {
  const initialOptions = useRef(options);
  const client = useMemo(
    () => supplied ?? createApprovalClient(initialOptions.current),
    [supplied],
  )
  const identity = useClientIdentity(client)
  const key = JSON.stringify([options.userKey?.() ?? supplied?.identity.value ?? '',
    supplied ? client.config().apiBase : resolveTransportOptions(options).apiBase])
  const [previousKey, setPreviousKey] = useState(key)
  useLayoutEffect(() => { setPreviousKey(key) }, [key])
  useLayoutEffect(() => {
    if (!supplied) client.updateOptions(options)
  }, [client, supplied, key, options])
  useEffect(() => {
    return () => {
      if (!supplied) client.dispose()
    }
  }, [client, supplied])
  useEffect(() => { void client.reloadUser() }, [client, identity])
  return (
    <ClientContext.Provider value={client}>
      <IdentityChangeContext.Provider value={previousKey !== key}>
      <TooltipProvider>
      <div key={key} className="approval-react">
        {children}
      </div>
      </TooltipProvider>
      </IdentityChangeContext.Provider>
    </ClientContext.Provider>
  )
}
export function useApprovalClient() {
  const client = useContext(ClientContext)
  if (!client) throw new Error('请先安装 ApprovalProvider')
  return client
}
export function useApprovalUser() {
  const client = useApprovalClient()
  const me = useSyncExternalStore(
    client.me.subscribe,
    client.me.getSnapshot,
    () => null,
  )
  const error = useSyncExternalStore(
    client.userError.subscribe,
    client.userError.getSnapshot,
    () => null,
  )
  const loading = useSyncExternalStore(
    client.userLoading.subscribe,
    client.userLoading.getSnapshot,
    () => false,
  )
  useClientIdentity(client)
  return {
    me,
    error,
    loading,
    userId: me?.userId ?? '',
    roles: me?.roles ?? [],
    capabilities: me?.capabilities ?? {},
    can: (permission: string) => me?.capabilities?.[permission] === true,
    isAdmin: me?.isAdmin ?? false,
    canViewDefinitions: me?.canViewDefinitions ?? false,
    formsEnabled: me?.formsEnabled ?? false,
    formBusinessType: me?.formBusinessType ?? null,
    reload: client.reloadUser,
  }
}
export function useApprovalIdentity() {
  const client = useApprovalClient()
  return useClientIdentity(client)
}
function useClientIdentity(client: ApprovalClient) {
  const user = useSyncExternalStore(
    client.identity.subscribe,
    client.identity.getSnapshot,
    client.identity.getSnapshot,
  )
  const version = useSyncExternalStore(
    client.version.subscribe,
    client.version.getSnapshot,
    client.version.getSnapshot,
  )
  return `${user}:${version}`
}
export function useDirectory() {
  const client = useApprovalClient()
  useApprovalIdentity()
  const roles = useSyncExternalStore(
    client.directory.roles.subscribe,
    client.directory.roles.getSnapshot,
    client.directory.roles.getSnapshot,
  )
  const available = useSyncExternalStore(
    client.directory.available.subscribe,
    client.directory.available.getSnapshot,
    client.directory.available.getSnapshot,
  )
  return { ...client.directory, roles, available }
}
