import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { getRoute, navigate, onRouteChange } from './router'

describe('router utilities', () => {
  const originalLocation = globalThis.window?.location

  beforeEach(() => {
    // Reset pathname for each test
    if (typeof window !== 'undefined') {
      window.history.pushState({}, '', '/login')
    }
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('getRoute returns current pathname', () => {
    window.history.pushState({}, '', '/dashboard')
    expect(getRoute()).toBe('/dashboard')
  })

  it('getRoute returns /login when window is undefined', () => {
    const win = globalThis.window
    // @ts-expect-error simulate SSR
    globalThis.window = undefined
    expect(getRoute()).toBe('/login')
    globalThis.window = win
  })

  it('navigate changes pathname and dispatches popstate', () => {
    const listener = vi.fn()
    window.addEventListener('popstate', listener)
    navigate('/dashboard')
    expect(window.location.pathname).toBe('/dashboard')
    expect(listener).toHaveBeenCalled()
    window.removeEventListener('popstate', listener)
  })

  it('navigate is no-op when window is undefined', () => {
    const win = globalThis.window
    // @ts-expect-error simulate SSR
    globalThis.window = undefined
    expect(() => navigate('/dashboard')).not.toThrow()
    globalThis.window = win
  })

  it('onRouteChange calls callback immediately and on popstate', () => {
    const cb = vi.fn()
    const unsub = onRouteChange(cb)
    expect(cb).toHaveBeenCalledWith('/login')

    window.history.pushState({}, '', '/players')
    window.dispatchEvent(new PopStateEvent('popstate'))
    expect(cb).toHaveBeenCalledWith('/players')

    unsub()
  })

  it('onRouteChange unsub removes listener', () => {
    const cb = vi.fn()
    const unsub = onRouteChange(cb)
    unsub()

    window.history.pushState({}, '', '/bans')
    window.dispatchEvent(new PopStateEvent('popstate'))
    // cb should not have been called again after unsub
    expect(cb).toHaveBeenCalledTimes(1)
  })

  it('onRouteChange handles undefined window gracefully', () => {
    const win = globalThis.window
    // @ts-expect-error simulate SSR
    globalThis.window = undefined
    const cb = vi.fn()
    const unsub = onRouteChange(cb)
    expect(cb).not.toHaveBeenCalled()
    expect(() => unsub()).not.toThrow()
    globalThis.window = win
  })
})
