import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { apiFetch } from './api'
import * as auth from './auth'

describe('apiFetch', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    vi.spyOn(auth, 'getToken').mockReturnValue('test-token-123')
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('sends request with Bearer token and Content-Type header', async () => {
    const mockFetch = vi.fn().mockResolvedValue({ status: 200, json: async () => ({}) })
    vi.stubGlobal('fetch', mockFetch)

    await apiFetch('/api/players')

    expect(mockFetch).toHaveBeenCalledTimes(1)
    const [, options] = mockFetch.mock.calls[0]
    const headers = options.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer test-token-123')
    expect(headers.get('Content-Type')).toBe('application/json')
  })

  it('does not send Authorization header when token is null', async () => {
    vi.spyOn(auth, 'getToken').mockReturnValue(null)
    const mockFetch = vi.fn().mockResolvedValue({ status: 200 })
    vi.stubGlobal('fetch', mockFetch)

    await apiFetch('/api/config')

    const [, options] = mockFetch.mock.calls[0]
    const headers = options.headers as Headers
    expect(headers.get('Authorization')).toBeNull()
  })

  it('prepends API_BASE to the path', async () => {
    const mockFetch = vi.fn().mockResolvedValue({ status: 200 })
    vi.stubGlobal('fetch', mockFetch)

    await apiFetch('/api/bans')

    const [url] = mockFetch.mock.calls[0]
    expect(url).toContain('/api/bans')
  })

  it('forwards custom options to fetch', async () => {
    const mockFetch = vi.fn().mockResolvedValue({ status: 201 })
    vi.stubGlobal('fetch', mockFetch)

    await apiFetch('/api/bans', { method: 'POST', body: JSON.stringify({ reason: 'test' }) })

    const [, options] = mockFetch.mock.calls[0]
    expect(options.method).toBe('POST')
    expect(options.body).toBe(JSON.stringify({ reason: 'test' }))
  })

  it('returns response on 401 without throwing', async () => {
    const mockResponse = { status: 401 }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(mockResponse))

    const res = await apiFetch('/api/config')
    expect(res.status).toBe(401)
  })

  it('merges custom headers with default headers', async () => {
    const mockFetch = vi.fn().mockResolvedValue({ status: 200 })
    vi.stubGlobal('fetch', mockFetch)

    await apiFetch('/api/players', { headers: { 'X-Custom': 'value' } })

    const [, options] = mockFetch.mock.calls[0]
    const headers = options.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer test-token-123')
    expect(headers.get('X-Custom')).toBe('value')
  })
})
