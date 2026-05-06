import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/svelte'
import Login from './Login.svelte'
import * as auth from '../lib/auth'

describe('Login', () => {
  let localStorageMock: Record<string, string> = {}

  beforeEach(() => {
    localStorageMock = {}
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
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders Access Token input and Sign In button', () => {
    render(Login)
    expect(screen.getByLabelText(/access token/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument()
  })

  it('stores valid token in localStorage and calls onSuccess', async () => {
    const onSuccess = vi.fn()
    render(Login, { props: { onSuccess } })

    const input = screen.getByLabelText(/access token/i)
    const button = screen.getByRole('button', { name: /sign in/i })

    await fireEvent.input(input, { target: { value: 'valid-token-123' } })
    await fireEvent.click(button)

    await waitFor(() => {
      expect(localStorageMock['sentinel_token']).toBe('valid-token-123')
      expect(onSuccess).toHaveBeenCalled()
    })
  })

  it('shows error for invalid token and does not write to localStorage', async () => {
    vi.spyOn(auth, 'validateToken').mockResolvedValue(false)
    const onSuccess = vi.fn()
    render(Login, { props: { onSuccess } })

    const input = screen.getByLabelText(/access token/i)
    const button = screen.getByRole('button', { name: /sign in/i })

    await fireEvent.input(input, { target: { value: 'bad-token' } })
    await fireEvent.click(button)

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/invalid token/i)
      expect(localStorageMock['sentinel_token']).toBeUndefined()
      expect(onSuccess).not.toHaveBeenCalled()
    })
  })

  it('shows error for empty token submission', async () => {
    const onSuccess = vi.fn()
    render(Login, { props: { onSuccess } })

    const button = screen.getByRole('button', { name: /sign in/i })
    await fireEvent.click(button)

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/please enter/i)
      expect(onSuccess).not.toHaveBeenCalled()
    })
  })
})
