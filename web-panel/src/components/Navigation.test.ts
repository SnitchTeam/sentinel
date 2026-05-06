import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/svelte'
import Navigation from './Navigation.svelte'

describe('Navigation', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  it('renders Sentinel brand link', () => {
    render(Navigation)
    expect(screen.getByLabelText(/sentinel home/i)).toBeInTheDocument()
  })

  it('renders desktop navigation links', () => {
    render(Navigation)
    expect(screen.getByText(/dashboard/i)).toBeInTheDocument()
    expect(screen.getByText(/players/i)).toBeInTheDocument()
    expect(screen.getByText(/bans/i)).toBeInTheDocument()
    expect(screen.getByText(/logs/i)).toBeInTheDocument()
    expect(screen.getByText(/config/i)).toBeInTheDocument()
  })

  it('renders sign out button', () => {
    render(Navigation)
    expect(screen.getByLabelText(/sign out/i)).toBeInTheDocument()
  })

  it('calls onLogout when sign out clicked', async () => {
    const onLogout = vi.fn()
    render(Navigation, { props: { onLogout } })

    const btn = screen.getByLabelText(/sign out/i)
    await fireEvent.click(btn)

    expect(onLogout).toHaveBeenCalled()
  })

  it('toggles mobile menu on hamburger click', async () => {
    render(Navigation)

    const hamburger = screen.getByLabelText(/open menu/i)
    await fireEvent.click(hamburger)

    expect(screen.getByLabelText(/close menu/i)).toBeInTheDocument()
    expect(screen.getAllByText(/dashboard/i).length).toBeGreaterThanOrEqual(2)
  })

  it('closes mobile menu when a link is clicked', async () => {
    render(Navigation)

    const hamburger = screen.getByLabelText(/open menu/i)
    await fireEvent.click(hamburger)

    const menuLinks = screen.getAllByText(/dashboard/i)
    // Click the mobile menu link
    await fireEvent.click(menuLinks[menuLinks.length - 1])

    // After clicking a link, mobile menu should close
    expect(screen.getByLabelText(/open menu/i)).toBeInTheDocument()
  })

  it('closes mobile menu when close button clicked', async () => {
    render(Navigation)

    const hamburger = screen.getByLabelText(/open menu/i)
    await fireEvent.click(hamburger)

    const closeBtn = screen.getByLabelText(/close menu/i)
    await fireEvent.click(closeBtn)

    expect(screen.getByLabelText(/open menu/i)).toBeInTheDocument()
  })

  it('renders mobile sign out in menu', async () => {
    render(Navigation)

    const hamburger = screen.getByLabelText(/open menu/i)
    await fireEvent.click(hamburger)

    const signOutButtons = screen.getAllByText(/sign out/i)
    expect(signOutButtons.length).toBeGreaterThanOrEqual(2)
  })

  it('calls onLogout from mobile menu', async () => {
    const onLogout = vi.fn()
    render(Navigation, { props: { onLogout } })

    const hamburger = screen.getByLabelText(/open menu/i)
    await fireEvent.click(hamburger)

    const signOutButtons = screen.getAllByText(/sign out/i)
    // Click mobile sign out
    await fireEvent.click(signOutButtons[signOutButtons.length - 1])

    expect(onLogout).toHaveBeenCalled()
  })
})
