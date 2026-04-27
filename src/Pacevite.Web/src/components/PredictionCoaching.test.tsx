import { screen, fireEvent } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionCoaching } from './PredictionCoaching'
import { describe, it, expect, vi, beforeEach } from 'vitest'

describe('PredictionCoaching', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  it('renders generate button initially', () => {
    renderWithProviders(<PredictionCoaching eventType="HYROX" />, { authenticated: true })
    expect(screen.getByRole('button', { name: /generate/i })).toBeInTheDocument()
  })

  it('does not show coaching text before generation', () => {
    renderWithProviders(<PredictionCoaching eventType="HYROX" />, { authenticated: true })
    expect(screen.queryByTestId('coaching-text')).not.toBeInTheDocument()
  })

  it('shows loading state while streaming', async () => {
    const neverResolves = new Promise<Response>(() => {})
    vi.mocked(fetch).mockReturnValue(neverResolves)

    renderWithProviders(<PredictionCoaching eventType="HYROX" />, { authenticated: true })
    fireEvent.click(screen.getByRole('button', { name: /generate/i }))

    expect(await screen.findByText(/generating/i)).toBeInTheDocument()
  })
})
