import { screen } from '@testing-library/react'
import { renderWithProviders } from '@/test/render'
import { PredictionChart } from './PredictionChart'
import type { PredictionDataPoint } from '@/lib/types'
import { describe, it, expect } from 'vitest'

const dataPoints: PredictionDataPoint[] = [
  { eventId: 'e1', eventDate: '2023-10-14', elapsedSecs: 4930, fittedSecs: 4920 },
  { eventId: 'e2', eventDate: '2024-03-09', elapsedSecs: 4724, fittedSecs: 4710 },
  { eventId: null, eventDate: '2026-04-25', elapsedSecs: null,  fittedSecs: 4320 },
]

describe('PredictionChart', () => {
  it('renders chart container', () => {
    renderWithProviders(<PredictionChart dataPoints={dataPoints} />, { authenticated: true })
    expect(screen.getByTestId('prediction-chart')).toBeInTheDocument()
  })

  it('renders empty message when no data', () => {
    renderWithProviders(<PredictionChart dataPoints={[]} />, { authenticated: true })
    expect(screen.getByTestId('prediction-chart-empty')).toBeInTheDocument()
  })
})
