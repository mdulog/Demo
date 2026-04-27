import { screen } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionCard } from './PredictionCard'
import type { PredictionResponse } from '@/lib/types'
import { describe, it, expect } from 'vitest'

const prediction: PredictionResponse = {
  eventType: 'HYROX',
  predictedSecs: 4320,
  confidenceLabel: 'High',
  avgImprovementSecs: 215,
  dataPoints: [],
}

describe('PredictionCard', () => {
  it('renders predicted time formatted correctly', () => {
    renderWithProviders(<PredictionCard prediction={prediction} />, { authenticated: true })
    // 4320s = 1:12:00
    expect(screen.getByTestId('prediction-time')).toHaveTextContent('1:12:00')
  })

  it('renders confidence badge', () => {
    renderWithProviders(<PredictionCard prediction={prediction} />, { authenticated: true })
    expect(screen.getByTestId('confidence-badge')).toHaveTextContent('High confidence')
  })

  it('renders avg improvement', () => {
    renderWithProviders(<PredictionCard prediction={prediction} />, { authenticated: true })
    // 215s = 3:35
    expect(screen.getByTestId('avg-improvement')).toHaveTextContent('3:35')
  })

  it('renders Medium confidence badge with different style', () => {
    const medium = { ...prediction, confidenceLabel: 'Medium' }
    renderWithProviders(<PredictionCard prediction={medium} />, { authenticated: true })
    const badge = screen.getByTestId('confidence-badge')
    expect(badge).toHaveTextContent('Medium confidence')
    expect(badge.className).toContain('bg-yellow')
  })
})
