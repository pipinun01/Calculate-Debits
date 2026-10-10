import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api, login, query, setShopId } from './client'

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const auth = (token: string) => json(200, { token, expiresAt: '2026-10-10T21:00:00Z', user: { id: 'u1', shops: [] } })

describe('api client', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
    setShopId(null)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('sends the bearer token and the shop header after login', async () => {
    fetchMock.mockResolvedValueOnce(auth('jwt-1')).mockResolvedValueOnce(json(200, { ok: true }))
    await login()
    setShopId('shop-1')

    await api('GET', '/api/customers')

    const [, init] = fetchMock.mock.calls[1]
    expect(init?.headers).toMatchObject({ Authorization: 'Bearer jwt-1', 'X-Shop-Id': 'shop-1' })
  })

  it('turns ProblemDetails into ApiError with the code', async () => {
    fetchMock.mockResolvedValueOnce(json(409, { title: 'customer.exists', detail: 'duplicate', status: 409, code: 'customer.exists' }))

    const error = await api('POST', '/api/customers', { name: 'A' }).catch((e) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 409, code: 'customer.exists', message: 'duplicate' })
  })

  it('signs in again once on 401 and repeats the request', async () => {
    fetchMock
      .mockResolvedValueOnce(json(401, { code: 'auth.unauthorized' }))
      .mockResolvedValueOnce(auth('jwt-2'))
      .mockResolvedValueOnce(json(200, { id: 'c1' }))

    await expect(api<{ id: string }>('GET', '/api/customers/c1')).resolves.toEqual({ id: 'c1' })

    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(fetchMock.mock.calls[1][0]).toBe('/api/auth/telegram')
    expect(fetchMock.mock.calls[2][1]?.headers).toMatchObject({ Authorization: 'Bearer jwt-2' })
  })

  it('does not loop when the second attempt is also 401', async () => {
    fetchMock.mockResolvedValueOnce(json(401, { code: 'auth.unauthorized' })).mockResolvedValueOnce(auth('jwt-3')).mockResolvedValueOnce(json(401, { code: 'auth.unauthorized' }))

    await expect(api('GET', '/api/me')).rejects.toMatchObject({ status: 401 })
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('posts FormData without forcing a JSON content type, and returns undefined for 204', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const form = new FormData()
    form.append('audio', new Blob(['x']), 'voice.webm')

    await expect(api('POST', '/api/entries/parse', form)).resolves.toBeUndefined()

    const [, init] = fetchMock.mock.calls[0]
    expect(init?.body).toBe(form)
    expect(init?.headers).not.toHaveProperty('Content-Type')
  })

  it('builds query strings without empty values', () => {
    expect(query({ filter: 'all', q: '', cursor: null, limit: 30 })).toBe('?filter=all&limit=30')
    expect(query({})).toBe('')
  })
})
