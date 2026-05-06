import { describe, it, expect, vi, beforeEach } from 'vitest'
import { getToken, setToken, clearToken, validateToken } from './auth'

describe('auth utilities', () => {
  let store: Record<string, string> = {}

  beforeEach(() => {
    store = {}
    Object.defineProperty(globalThis, 'localStorage', {
      value: {
        getItem: vi.fn((key: string) => store[key] ?? null),
        setItem: vi.fn((key: string, value: string) => { store[key] = value }),
        removeItem: vi.fn((key: string) => { delete store[key] }),
      },
      writable: true,
      configurable: true,
    })
  })

  it('getToken returns null when empty', () => {
    expect(getToken()).toBeNull()
  })

  it('setToken stores in localStorage', () => {
    setToken('abc123')
    expect(store['sentinel_token']).toBe('abc123')
  })

  it('clearToken removes from localStorage', () => {
    setToken('abc123')
    clearToken()
    expect(store['sentinel_token']).toBeUndefined()
  })

  it('validateToken returns true for long tokens when API unreachable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network')))
    const result = await validateToken('long-enough-token')
    expect(result).toBe(true)
  })

  it('validateToken returns false for short tokens when API unreachable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network')))
    const result = await validateToken('short')
    expect(result).toBe(false)
  })

  it('validateToken returns true on HTTP 200', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 200 }))
    const result = await validateToken('valid-token')
    expect(result).toBe(true)
  })

  it('validateToken returns false on HTTP 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 401 }))
    const result = await validateToken('invalid-token')
    expect(result).toBe(false)
  })
})
