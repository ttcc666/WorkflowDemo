import { currentUserId } from '@/stores/session'

/** 后端返回的业务错误（ProblemDetails），message 为可直接展示给用户的中文提示。 */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
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
  const headers: Record<string, string> = { 'X-User-Id': currentUserId.value }
  let body: string | undefined
  if (options.rawJson !== undefined) body = options.rawJson
  else if (options.body !== undefined) body = JSON.stringify(options.body)
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(url, { method, headers, body, signal: options.signal })

  if (!response.ok) {
    let message = `请求失败（HTTP ${response.status}）`
    let code: string | undefined
    try {
      const problem = await response.json()
      message = problem.title ?? message
      code = problem.code
    } catch {
      // 非 JSON 错误响应，使用默认提示
    }
    throw new ApiError(message, response.status, code)
  }

  if (response.status === 204) return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const http = {
  get: <T>(url: string, signal?: AbortSignal) => request<T>('GET', url, { signal }),
  post: <T = void>(url: string, body?: unknown) => request<T>('POST', url, { body }),
  postJson: <T = void>(url: string, json: string) => request<T>('POST', url, { rawJson: json }),
  put: <T = void>(url: string, body?: unknown) => request<T>('PUT', url, { body }),
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
