import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/svelte'
import StatsCard from './StatsCard.svelte'

describe('StatsCard', () => {
  it('renders label and value', () => {
    render(StatsCard, { props: { label: 'Online Players', value: 42 } })
    expect(screen.getByText('Online Players')).toBeInTheDocument()
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  it('renders change text when provided', () => {
    render(StatsCard, { props: { label: 'Bans', value: 7, change: '-1', tone: 'neutral' } })
    expect(screen.getByText('-1')).toBeInTheDocument()
  })

  it('does not render change element when change is undefined', () => {
    render(StatsCard, { props: { label: 'FPS', value: 120 } })
    expect(screen.queryByText(/stable/i)).not.toBeInTheDocument()
  })

  it('applies success tone class', () => {
    render(StatsCard, { props: { label: 'Online', value: 10, change: '+3', tone: 'success' } })
    const changeEl = screen.getByText('+3')
    expect(changeEl).toHaveClass('text-sentinel-success')
  })

  it('applies danger tone class', () => {
    render(StatsCard, { props: { label: 'Flags', value: 5, change: '+5', tone: 'danger' } })
    const changeEl = screen.getByText('+5')
    expect(changeEl).toHaveClass('text-sentinel-danger')
  })

  it('applies neutral tone class by default', () => {
    render(StatsCard, { props: { label: 'FPS', value: 120, change: 'stable' } })
    const changeEl = screen.getByText('stable')
    expect(changeEl).toHaveClass('text-sentinel-secondary')
  })

  it('renders string values correctly', () => {
    render(StatsCard, { props: { label: 'Status', value: 'OK' } })
    expect(screen.getByText('OK')).toBeInTheDocument()
  })
})
