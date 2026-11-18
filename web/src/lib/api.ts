import { useAuthStore } from '@/stores/auth'
import type { AuthResponse, ProblemDetails } from './types'

export class ApiError extends Error {
  readonly status: number
  readonly problem?: ProblemDetails

  constructor(status: number, problem?: ProblemDetails) {
    const fieldErrors = problem?.errors ? Object.values(problem.errors).flat().join(' ') : undefined
    super(fieldErrors || problem?.detail || problem?.title || `Request failed (${status})`)
    this.status = status
    this.problem = problem
  }
}

let refreshing: Promise<boolean> | null = null

/** Rotates the refresh token once, shared by concurrent callers (single flight). */
async function refreshSession(): Promise<boolean> {
  const { refreshToken, setSession, clear } = useAuthStore.getState()
  if (!refreshToken) return false

  refreshing ??= fetch('/api/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  })
    .then(async (response) => {
      if (!response.ok) {
        clear()
        return false
      }
      setSession((await response.json()) as AuthResponse)
      return true
    })
    .finally(() => {
      refreshing = null
    })

  return refreshing
}

/** fetch() with bearer auth and transparent token refresh on 401. Returns the raw Response. */
export async function authFetch(path: string, init: RequestInit = {}, retry = true): Promise<Response> {
  const token = useAuthStore.getState().accessToken
  const headers = new Headers(init.headers)
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(path, { ...init, headers })
  if (response.status === 401 && retry && (await refreshSession())) {
    return authFetch(path, init, false)
  }
  return response
}

async function parse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let problem: ProblemDetails | undefined
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      /* empty body */
    }
    throw new ApiError(response.status, problem)
  }
  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string) => authFetch(path).then((r) => parse<T>(r)),
  post: <T>(path: string, body?: unknown) =>
    authFetch(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }).then((r) => parse<T>(r)),
  put: <T>(path: string, body?: unknown) =>
    authFetch(path, { method: 'PUT', body: JSON.stringify(body) }).then((r) => parse<T>(r)),
  delete: <T = void>(path: string) => authFetch(path, { method: 'DELETE' }).then((r) => parse<T>(r)),
  upload: <T>(path: string, files: File[]) => {
    const form = new FormData()
    files.forEach((file) => form.append('files', file))
    return authFetch(path, { method: 'POST', body: form }).then((r) => parse<T>(r))
  },
  /** Anonymous auth calls (no bearer, no refresh). */
  auth: <T>(path: string, body: unknown) =>
    fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).then((r) =>
      parse<T>(r),
    ),
}
