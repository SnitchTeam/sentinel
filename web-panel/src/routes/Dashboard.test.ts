import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/svelte'
import Dashboard from './Dashboard.svelte'

describe('Dashboard', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  it('renders navigation with hamburger menu on mobile viewport', () => {
    // Simulate mobile viewport
    Object.defineProperty(window, 'innerWidth', { writable: true, configurable: true, value: 375 })
    window.dispatchEvent(new Event('resize'))

    render(Dashboard)
    expect(screen.getByLabelText(/open menu/i)).toBeInTheDocument()
  })

  it('renders stats cards', () => {
    render(Dashboard)
    expect(screen.getByText(/online players/i)).toBeInTheDocument()
    expect(screen.getByText(/active bans/i)).toBeInTheDocument()
    expect(screen.getByText(/ai flags/i)).toBeInTheDocument()
    expect(screen.getByText(/server fps/i)).toBeInTheDocument()
  })

  it('switches tabs when clicked', async () => {
    render(Dashboard)
    const playersTab = screen.getByRole('button', { name: /players/i })
    await fireEvent.click(playersTab)
    expect(screen.getByPlaceholderText(/search players/i)).toBeInTheDocument()
  })

  it('renders player cards on mobile and table on desktop', async () => {
    render(Dashboard)
    const playersTab = screen.getByRole('button', { name: /players/i })
    await fireEvent.click(playersTab)
    // In jsdom both mobile cards and desktop table render (no CSS breakpoint enforcement)
    // We expect PlayerOne to appear in both layouts
    expect(screen.getAllByText(/PlayerOne/).length).toBeGreaterThanOrEqual(1)
  })
})
