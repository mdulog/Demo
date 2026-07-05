import { describe, it, expect } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { EventDetailPage } from '@/pages/EventDetailPage'
import { renderWithProviders } from '@/test/render'
import { Route, Routes } from 'react-router-dom'

function renderDetail(id = 'event-1') {
  return renderWithProviders(
    <Routes>
      <Route path="/events/:id" element={<EventDetailPage />} />
    </Routes>,
    { initialEntries: [`/events/${id}`], authenticated: true }
  )
}

describe('EventDetailPage', () => {
  it('renders the event name and date', async () => {
    // Arrange / Act
    renderDetail()

    // Assert
    await waitFor(() => {
      expect(screen.getByText('Berlin Marathon')).toBeInTheDocument()
    })
    expect(screen.getByText(/2024-09-29/)).toBeInTheDocument()
  })

  it('renders the split chart when splits exist', async () => {
    // Arrange / Act
    renderDetail()

    // Assert
    await waitFor(() => {
      expect(screen.getByTestId('split-chart')).toBeInTheDocument()
    })
  })

  it('renders the race comparison section', async () => {
    // Arrange / Act
    renderDetail()

    // Assert — RaceComparison always mounts its container; shows "need more data" with 1 event
    await waitFor(() => {
      expect(screen.getByTestId('race-comparison')).toBeInTheDocument()
    })
  })

  it('shows no-splits message when event has no splits', async () => {
    // Arrange
    server.use(
      // Registered first so it wins over the /:id override below — MSW's runtime
      // handlers are matched in registration order, and "timeline" would otherwise
      // match the /:id pattern.
      http.get('http://localhost/api/events/timeline', () => HttpResponse.json([])),
      http.get('http://localhost/api/events/:id', () =>
        HttpResponse.json({
          id: 'event-nosplits',
          eventType: 'MARATHON',
          eventName: 'Berlin Marathon',
          eventDate: '2024-09-29',
          completion: 'FINISHED',
          elapsedSecs: 14400,
          overallRank: null,
          ageGroupRank: null,
          fieldSize: null,
          ageGroupFieldSize: null,
          source: 'manual',
          createdAt: '2024-10-01T00:00:00Z',
          splits: [],
          averageSplits: [],
        })
      )
    )

    // Act
    renderDetail('event-nosplits')

    // Assert
    await waitFor(() => {
      expect(screen.getByTestId('split-chart-empty')).toBeInTheDocument()
    })
  })

  it('shows a needs-enrichment banner when the event is missing placement data', async () => {
    // Arrange
    server.use(
      // Registered first so it wins over the /:id override below — MSW's runtime
      // handlers are matched in registration order, and "timeline" would otherwise
      // match the /:id pattern.
      http.get('http://localhost/api/events/timeline', () => HttpResponse.json([])),
      http.get('http://localhost/api/events/:id', () =>
        HttpResponse.json({
          id: 'event-gpx-1',
          eventType: 'GENERIC',
          eventName: 'Morning Run',
          eventDate: '2026-05-01',
          completion: 'FINISHED',
          elapsedSecs: 2700,
          overallRank: null,
          ageGroupRank: null,
          fieldSize: null,
          ageGroupFieldSize: null,
          source: 'GPX',
          needsEnrichment: true,
          createdAt: '2026-05-01T00:00:00Z',
          splits: [],
          averageSplits: [],
        })
      )
    )

    // Act
    renderDetail('event-gpx-1')

    // Assert
    await waitFor(() => {
      expect(screen.getByText(/needs enrichment/i)).toBeInTheDocument()
    })
  })

  it('does not show a needs-enrichment banner for complete events', async () => {
    // Arrange / Act
    renderDetail()

    // Assert
    await waitFor(() => {
      expect(screen.getByText('Berlin Marathon')).toBeInTheDocument()
    })
    expect(screen.queryByText(/needs enrichment/i)).not.toBeInTheDocument()
  })

  it('shows loading state initially', () => {
    // Arrange / Act
    renderDetail()

    // Assert
    expect(screen.getByText(/loading/i)).toBeInTheDocument()
  })
})
