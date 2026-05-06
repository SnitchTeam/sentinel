import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte'
import { tick } from 'svelte'
import App from './App.svelte'
import * as auth from './lib/auth'
import * as router from './lib/router'

describe('App', () => {
  let localStorageMock: Record<string, string> = {}
  let routeCallbacks: ((route: string) => void)[] = []

  beforeEach(() => {
    localStorageMock = {}
    routeCallbacks = []
    Object.defineProperty(globalThis, 'localStorage', {
      value: {
        getItem: vi.fn((key: string) => localStorageMock[key] ?? null),
        setItem: vi.fn((key: string, value: string) => { localStorageMock[key] = value }),
        removeItem: vi.fn((key: string) => { delete localStorageMock[key] }),
      },
      writable: true,
      configurable: true,
    })
    vi.spyOn(auth, 'validateToken').mockResolvedValue(true)
    vi.spyOn(router, 'navigate').mockImplementation((path) => {
      vi.spyOn(router, 'getRoute').mockReturnValue(path)
      routeCallbacks.forEach((cb) => cb(path))
    })
    vi.spyOn(router, 'onRouteChange').mockImplementation((cb) => {
      routeCallbacks.push(cb)
      return () => {
        routeCallbacks = routeCallbacks.filter((c) => c !== cb)
      }
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  function setRoute(route: string) {
    vi.spyOn(router, 'getRoute').mockReturnValue(route)
    routeCallbacks.forEach((cb) => cb(route))
  }

  it('renders Login on /login route', () => {
    setRoute('/login')
    render(App)
    expect(screen.getByLabelText(/access token/i)).toBeInTheDocument()
  })

  it('renders Login on / route', () => {
    setRoute('/')
    render(App)
    expect(screen.getByLabelText(/access token/i)).toBeInTheDocument()
  })

  it('renders Dashboard when authenticated', async () => {
    localStorageMock['sentinel_token'] = 'valid-token'
    setRoute('/dashboard')
    render(App)
    await tick()
    expect(screen.getByText(/online players/i)).toBeInTheDocument()
  })

  it('redirects unauthenticated users from /dashboard to /login', async () => {
    setRoute('/dashboard')
    const navigateSpy = vi.spyOn(router, 'navigate').mockImplementation(() => {})
    render(App)
    await tick()
    await waitFor(() => {
      expect(navigateSpy).toHaveBeenCalledWith('/login')
    })
  })

  it('shows 404 for unknown routes', async () => {
    localStorageMock['sentinel_token'] = 'valid-token'
    setRoute('/unknown-route')
    render(App)
    await tick()
    expect(screen.getByText(/404/i)).toBeInTheDocument()
  })

  it('logs out and shows login when logout triggered', async () => {
    localStorageMock['sentinel_token'] = 'valid-token'
    setRoute('/dashboard')
    render(App)
    await tick()

    const logoutBtn = screen.getByLabelText(/sign out/i)
    await fireEvent.click(logoutBtn)
    await tick()

    await waitFor(() => {
      expect(screen.getByLabelText(/access token/i)).toBeInTheDocument()
    })
  })
})
