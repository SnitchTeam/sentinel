import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/svelte'
import PlayerTable from './PlayerTable.svelte'

describe('PlayerTable', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    vi.spyOn(console, 'log').mockImplementation(() => {})
  })

  it('renders all player names', () => {
    render(PlayerTable)
    expect(screen.getAllByText('PlayerOne').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('RustLord').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('SneakyBeaky').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('BaseBuilder').length).toBeGreaterThanOrEqual(1)
  })

  it('renders all steam IDs', () => {
    render(PlayerTable)
    expect(screen.getAllByText('76561198012345678').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('76561198087654321').length).toBeGreaterThanOrEqual(1)
  })

  it('renders ping values', () => {
    render(PlayerTable)
    expect(screen.getAllByText('24ms').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('56ms').length).toBeGreaterThanOrEqual(1)
  })

  it('shows violation scores for flagged players', () => {
    render(PlayerTable)
    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
  })

  it('shows Clean for players with zero violations', () => {
    render(PlayerTable)
    const cleanElements = screen.getAllByText('Clean')
    expect(cleanElements.length).toBeGreaterThanOrEqual(2)
  })

  it('renders warn buttons for each player', () => {
    render(PlayerTable)
    const warnButtons = screen.getAllByRole('button', { name: /warn/i })
    expect(warnButtons.length).toBeGreaterThanOrEqual(4)
  })

  it('renders kick buttons for each player', () => {
    render(PlayerTable)
    const kickButtons = screen.getAllByRole('button', { name: /kick/i })
    expect(kickButtons.length).toBeGreaterThanOrEqual(4)
  })

  it('renders ban buttons for each player', () => {
    render(PlayerTable)
    const banButtons = screen.getAllByRole('button', { name: /ban/i })
    expect(banButtons.length).toBeGreaterThanOrEqual(4)
  })

  it('logs action on warn click', async () => {
    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => {})
    render(PlayerTable)

    const warnButtons = screen.getAllByRole('button', { name: /warn/i })
    await fireEvent.click(warnButtons[0])

    expect(logSpy).toHaveBeenCalledWith(expect.stringContaining('warn'))
  })

  it('logs action on kick click', async () => {
    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => {})
    render(PlayerTable)

    const kickButtons = screen.getAllByRole('button', { name: /kick/i })
    await fireEvent.click(kickButtons[0])

    expect(logSpy).toHaveBeenCalledWith(expect.stringContaining('kick'))
  })

  it('logs action on ban click', async () => {
    const logSpy = vi.spyOn(console, 'log').mockImplementation(() => {})
    render(PlayerTable)

    const banButtons = screen.getAllByRole('button', { name: /ban/i })
    await fireEvent.click(banButtons[0])

    expect(logSpy).toHaveBeenCalledWith(expect.stringContaining('ban'))
  })
})
