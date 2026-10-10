import type { AuthResponse } from './types'
import { initData } from '../lib/telegram'

/** Backend ProblemDetails turned into an exception; `code` is the i18n key suffix (errors.<code>). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fields?: Record<string, string[]>

  constructor(status: number, code: string, message: string, fields?: Record<string, string[]>) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fields = fields
  }
}

let token: string | null = null
let shopId: string | null = null
let pendingLogin: Promise<AuthResponse> | null = null

export const setShopId = (id: string | null): void => {
  shopId = id
}

export const getShopId = (): string | null => shopId

async function parseError(response: Response): Promise<ApiError> {
  let code = 'unknown'
  let message = response.statusText
  let fields: Record<string, string[]> | undefined
  try {
    const body = await response.json()
    code = body.code ?? code
    message = body.detail ?? body.title ?? message
    fields = body.errors
  } catch {
    /* not JSON */
  }
  return new ApiError(response.status, code, message, fields)
}

/** Exchanges Telegram initData for a JWT. In `vite dev` outside Telegram it uses the backend's Development bypass header. */
export function login(): Promise<AuthResponse> {
  pendingLogin ??= (async () => {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' }
    const data = initData()
    if (!data && import.meta.env.DEV) {
      // Browser work without Telegram: localStorage.dev_tg_id / dev_first_name switch the test user without a restart.
      headers['X-Dev-Telegram-Id'] = localStorage.getItem('dev_tg_id') ?? import.meta.env.VITE_DEV_TELEGRAM_ID ?? '1000001'
      headers['X-Dev-First-Name'] = localStorage.getItem('dev_first_name') ?? import.meta.env.VITE_DEV_FIRST_NAME ?? 'Dev'
    }
    const response = await fetch('/api/auth/telegram', { method: 'POST', headers, body: JSON.stringify({ initData: data }) })
    if (!response.ok) throw await parseError(response)
    const auth = (await response.json()) as AuthResponse
    token = auth.token
    return auth
  })().finally(() => {
    pendingLogin = null
  })
  return pendingLogin
}

type Body = Record<string, unknown> | FormData | undefined

async function send(method: string, path: string, body: Body, retry: boolean): Promise<Response> {
  const headers: Record<string, string> = {}
  if (token) headers.Authorization = `Bearer ${token}`
  if (shopId) headers['X-Shop-Id'] = shopId
  let payload: BodyInit | undefined
  if (body instanceof FormData) {
    payload = body
  } else if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
    payload = JSON.stringify(body)
  }

  const response = await fetch(path, { method, headers, body: payload })
  // The JWT lives 12 hours; a Mini App left open overnight signs in again transparently.
  if (response.status === 401 && retry) {
    await login()
    return send(method, path, body, false)
  }
  return response
}

export async function api<T>(method: 'GET' | 'POST' | 'PATCH' | 'DELETE', path: string, body?: Body): Promise<T> {
  const response = await send(method, path, body, true)
  if (!response.ok) throw await parseError(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** Fetches a file with the session's credentials; the name comes from Content-Disposition. */
export async function download(path: string): Promise<{ blob: Blob; fileName: string }> {
  const response = await send('GET', path, undefined, true)
  if (!response.ok) throw await parseError(response)
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1]
  const plain = /filename="?([^";]+)"?/i.exec(disposition)?.[1]
  return { blob: await response.blob(), fileName: encoded ? decodeURIComponent(encoded) : (plain ?? 'export.xlsx') }
}

/** "?a=1&b=2" from an object, skipping empty values. */
export function query(params: Record<string, string | number | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') search.set(key, String(value))
  }
  const s = search.toString()
  return s ? `?${s}` : ''
}
