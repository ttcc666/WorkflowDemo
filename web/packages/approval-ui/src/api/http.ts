import { approvalConfig } from '../config'

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
  signal?: AbortSignal
}

async function request<T>(method: string, url: string, options: RequestOptions = {}): Promise<T> {
  const config = approvalConfig()
  const headers: Record<string, string> = { ...config.headers?.() }
  let body: string | undefined
  if (options.rawJson !== undefined) body = options.rawJson
  else if (options.body !== undefined) body = JSON.stringify(options.body)
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(url, { method, headers, body, signal: options.signal })

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
    if (response.status === 401) config.onUnauthorized?.()
    throw new ApiError(message, response.status, code, errors)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/** 基础 HTTP 客户端：附加配置的请求头，把 ProblemDetails 转为 {@link ApiError}。宿主的业务接口也可使用。 */
export const http = {
  get: <T>(url: string, signal?: AbortSignal) => request<T>('GET', url, { signal }),
  post: <T = void>(url: string, body?: unknown) => request<T>('POST', url, { body }),
  postJson: <T = void>(url: string, json: string) => request<T>('POST', url, { rawJson: json }),
  put: <T = void>(url: string, body?: unknown) => request<T>('PUT', url, { body }),
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
