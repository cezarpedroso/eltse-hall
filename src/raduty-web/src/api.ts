import { apiScope, developmentUser, isDevelopmentAuth, msalInstance } from './auth'
import type { ProblemDetails } from './types'

const baseUrl = (import.meta.env.VITE_API_BASE_URL || 'https://localhost:7068').replace(/\/$/, '')

export class ApiError extends Error {
  readonly problem: ProblemDetails
  constructor(problem: ProblemDetails) { super(problem.title); this.problem = problem }
}

async function accessToken(): Promise<string | undefined> {
  if (isDevelopmentAuth) return undefined
  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0]
  if (!account) return undefined
  const response = await msalInstance.acquireTokenSilent({ account, scopes: [apiScope] })
  return response.accessToken
}

export async function api<T>(path: string, init: RequestInit = {}, signal?: AbortSignal): Promise<T> {
  const token = await accessToken()
  const headers = new Headers(init.headers)
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (isDevelopmentAuth) headers.set('X-Dev-User', developmentUser)
  const response = await fetch(`${baseUrl}${path}`, { ...init, headers, signal })
  if (!response.ok) {
    let problem: ProblemDetails
    try { problem = await response.json() as ProblemDetails }
    catch { problem = { status: response.status, title: 'The request could not be completed.', code: 'REQUEST_FAILED' } }
    if (response.status === 401 && !isDevelopmentAuth) await msalInstance.loginRedirect({ scopes: [apiScope] })
    throw new ApiError(problem)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export async function downloadPdf(path: string, filename: string): Promise<void> {
  const token = await accessToken()
  const headers = new Headers()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (isDevelopmentAuth) headers.set('X-Dev-User', developmentUser)
  const response = await fetch(`${baseUrl}${path}`, { headers })
  if (!response.ok) {
    const problem = await response.json() as ProblemDetails
    throw new ApiError(problem)
  }
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url; link.download = filename; link.click()
  URL.revokeObjectURL(url)
}
