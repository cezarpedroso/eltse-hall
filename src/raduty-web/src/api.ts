import type { ProblemDetails } from './types'

const configuredBaseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.trim()
const baseUrl = configuredBaseUrl ? configuredBaseUrl.replace(/\/$/, '') : ''
let csrfRequest: Promise<string> | undefined

export class ApiError extends Error {
  readonly problem: ProblemDetails
  constructor(problem: ProblemDetails) { super(problem.title); this.problem = problem }
}

function isUnsafe(method?: string) {
  return !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes((method ?? 'GET').toUpperCase())
}

async function csrfToken(): Promise<string> {
  if (import.meta.env.MODE === 'test') return 'test-csrf-token'
  if (!csrfRequest) {
    csrfRequest = fetch(`${baseUrl}/api/auth/csrf`, { credentials: 'include', cache: 'no-store' })
      .then(async (response) => {
        if (!response.ok) throw new Error('The secure session could not be initialized.')
        const body = await response.json() as { token?: string }
        if (!body.token) throw new Error('The secure session token is missing.')
        return body.token
      })
      .catch((error) => { csrfRequest = undefined; throw error })
  }
  return csrfRequest
}

export function resetCsrfToken() { csrfRequest = undefined }

async function request(path: string, init: RequestInit, signal?: AbortSignal, allowCsrfRetry = true): Promise<Response> {
  const headers = new Headers(init.headers)
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (isUnsafe(init.method)) headers.set('X-CSRF-TOKEN', await csrfToken())
  const response = await fetch(`${baseUrl}${path}`, { ...init, headers, signal, credentials: 'include', cache: isUnsafe(init.method) ? init.cache : 'no-store' })
  const contentType = response.headers.get('content-type') ?? ''
  if (response.status === 400 && isUnsafe(init.method) && allowCsrfRetry && !contentType.includes('application/problem+json')) {
    resetCsrfToken()
    return request(path, init, signal, false)
  }
  return response
}

export async function api<T>(path: string, init: RequestInit = {}, signal?: AbortSignal): Promise<T> {
  const response = await request(path, init, signal)
  if (!response.ok) {
    let problem: ProblemDetails
    try { problem = await response.json() as ProblemDetails }
    catch { problem = { status: response.status, title: 'The request could not be completed.', code: 'REQUEST_FAILED' } }
    throw new ApiError(problem)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export async function signIn(email: string, password: string, rememberMe: boolean): Promise<{ mustChangePassword: boolean }> {
  const result = await api<{ mustChangePassword: boolean }>('/api/auth/login', {
    method: 'POST', body: JSON.stringify({ email, password, rememberMe }),
  })
  resetCsrfToken()
  return result
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await api('/api/auth/change-password', {
    method: 'POST', body: JSON.stringify({ currentPassword, newPassword }),
  })
  resetCsrfToken()
}

export async function signOut(): Promise<void> {
  try { await api('/api/auth/logout', { method: 'POST' }) }
  finally { resetCsrfToken() }
}

export async function downloadPdf(path: string, filename: string): Promise<void> {
  const response = await fetch(`${baseUrl}${path}`, { credentials: 'include' })
  if (!response.ok) {
    let problem: ProblemDetails
    try { problem = await response.json() as ProblemDetails }
    catch { problem = { status: response.status, title: 'The download could not be completed.', code: 'REQUEST_FAILED' } }
    throw new ApiError(problem)
  }
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url; link.download = filename; link.click()
  URL.revokeObjectURL(url)
}
