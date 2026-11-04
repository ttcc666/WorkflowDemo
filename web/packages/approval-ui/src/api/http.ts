import { approvalIdentityKey, type ResolvedApprovalUIOptions } from '../config'

/** 后端返回的业务错误（ProblemDetails），message 为可直接展示给用户的中文提示。 */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
    /** 字段级校验错误（400 ValidationProblemDetails）：字段路径 → 错误信息 */
    readonly errors?: Record<string, string[]>,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

interface RequestOptions {
  body?: unknown
  /** 请求体已是 JSON 字符串（如流程定义），原样发送 */
  rawJson?: string
  /** 原始请求体（不设置 JSON 类型） */
  raw?: Blob
  headers?: Record<string, string>
  asBlob?: boolean
  signal?: AbortSignal
}

/** 创建独立的传输客户端；请求时读取 headers、fetcher 与错误处理。 */
export function createHttpClient(
  getConfig: () => Pick<ResolvedApprovalUIOptions, 'headers' | 'fetcher' | 'onUnauthorized' | 'userKey'>,
) {
  const pending = new Set<AbortController>()
  async function request<T>(method: string, url: string, options: RequestOptions = {}): Promise<T> {
    const config = getConfig()
    const identity = approvalIdentityKey(config)
    const controller = new AbortController()
    pending.add(controller)
    function verifyIdentity() {
      if (controller.signal.aborted || identity !== approvalIdentityKey(getConfig())) throw new DOMException('身份已切换或请求已取消', 'AbortError')
    }
    try {
    const headers: Record<string, string> = { ...config.headers?.(), ...options.headers }
    let body: string | Blob | undefined
    if (options.raw !== undefined) body = options.raw
    else if (options.rawJson !== undefined) body = options.rawJson
    else if (options.body !== undefined) body = JSON.stringify(options.body)
    if (typeof body === 'string') headers['Content-Type'] = 'application/json'

    const signal = options.signal ? AbortSignal.any([controller.signal, options.signal]) : controller.signal
    const response = await (config.fetcher ?? fetch)(url, { method, headers, body, signal })
    verifyIdentity()

    if (!response.ok) {
      let message = `请求失败（HTTP ${response.status}）`
      let code: string | undefined
      let errors: Record<string, string[]> | undefined
      try {
        const problem = await response.json()
        message = problem.title ?? message
        code = problem.code
        errors = problem.errors
      } catch {
        // 非 JSON 错误响应，使用默认提示
      }
      verifyIdentity()
      if (response.status === 401) config.onUnauthorized?.()
      throw new ApiError(message, response.status, code, errors)
    }

    if (response.status === 204) return undefined as T
    if (options.asBlob) { const blob = await response.blob(); verifyIdentity(); return blob as T }
    const text = await response.text()
    verifyIdentity()
    return (text ? JSON.parse(text) : undefined) as T
    } finally { pending.delete(controller) }
  }

  /** 基础 HTTP 客户端：附加配置的请求头，把 ProblemDetails 转为 {@link ApiError}。宿主的业务接口也可使用。 */
  return {
    abortPending: () => { for (const controller of pending) controller.abort(); pending.clear() },
    get: <T>(url: string, signal?: AbortSignal) => request<T>('GET', url, { signal }),
    post: <T = void>(url: string, body?: unknown, headers?: Record<string, string>) => request<T>('POST', url, { body, headers }),
    postJson: <T = void>(url: string, json: string, headers?: Record<string, string>) => request<T>('POST', url, { rawJson: json, headers }),
    put: <T = void>(url: string, body?: unknown, headers?: Record<string, string>) => request<T>('PUT', url, { body, headers }),
    delete: <T = void>(url: string, headers?: Record<string, string>) => request<T>('DELETE', url, { headers }),
    /** 以原始请求体上传（如文件内容），headers 附加在配置的请求头之后 */
    upload: <T>(url: string, body: Blob, headers: Record<string, string>) => request<T>('POST', url, { raw: body, headers }),
    /** 下载为 Blob（带配置的请求头，用于需要鉴权的文件下载） */
    blob: (url: string) => request<Blob>('GET', url, { asBlob: true }),
  }
}

export type ApprovalHttpClient = ReturnType<typeof createHttpClient>

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
