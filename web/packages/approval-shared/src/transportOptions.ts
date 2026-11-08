export interface ApprovalTransportOptions {
  apiBase?: string
  headers?: () => Record<string, string>
  fetcher?: (url: string, init: RequestInit) => Promise<Response>
  onUnauthorized?: () => void
  userKey?: () => string | undefined
}
export interface ResolvedTransportOptions extends ApprovalTransportOptions { apiBase: string }
export const approvalIdentityKey = (options: Pick<ApprovalTransportOptions, 'userKey'>) => options.userKey?.() ?? ''
export function resolveTransportOptions(options: ApprovalTransportOptions = {}): ResolvedTransportOptions {
  return { ...options, apiBase: (options.apiBase ?? '/api/approval').replace(/\/+$/, '') }
}
