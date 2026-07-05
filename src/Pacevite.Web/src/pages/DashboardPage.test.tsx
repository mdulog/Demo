import { describe, it, expect } from 'vitest'
import { screen, within, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { server } from '@/test/handlers'
import { DashboardPage } from '@/pages/DashboardPage'
import { renderWithProviders } from '@/test/render'

function renderDashboard() {
  return renderWithProviders(<DashboardPage />, { authenticated: true })
}

describe('DashboardPage', () => {
  it('renders the event list from the API', async () => {
    renderDashboard()

    const heading = await screen.findByRole('heading', { name: /all events/i })
    const section = heading.closest('section')!

    await waitFor(() => {
      expect(within(section).getByText('Berlin Marathon')).toBeInTheDocument()
    })
    expect(within(section).getByText('2024-09-29')).toBeInTheDocument()
  })

  it('renders personal bests section when data is present', async () => {
    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText(/personal bests/i)).toBeInTheDocument()
    })
    // MARATHON appears in: personal bests card, event list row, and PbPanel button
    await waitFor(() => {
      expect(screen.getAllByText('MARATHON')).toHaveLength(3)
    })
  })

  it('shows empty state when no events exist', async () => {
    server.use(
      http.get('http://localhost/api/events', () => HttpResponse.json({ items: [], nextCursor: null })),
      http.get('http://localhost/api/events/timeline', () => HttpResponse.json([])),
      http.get('http://localhost/api/events/personal-bests', () => HttpResponse.json([]))
    )

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('No events yet.')).toBeInTheDocument()
    })
  })

  it('shows progress chart panel when events are present', async () => {
    // Arrange / Act
    renderDashboard()

    // Assert
    await waitFor(() => {
      expect(screen.getByTestId('progress-chart-panel')).toBeInTheDocument()
    })
  })

  it('shows empty state in progress chart when no events exist', async () => {
    // Arrange
    server.use(
      http.get('http://localhost/api/events', () => HttpResponse.json({ items: [], nextCursor: null })),
      http.get('http://localhost/api/events/timeline', () => HttpResponse.json([])),
      http.get('http://localhost/api/events/personal-bests', () => HttpResponse.json([]))
    )

    // Act
    renderDashboard()

    // Assert
    await waitFor(() => {
      expect(screen.getByTestId('progress-chart-empty')).toBeInTheDocument()
    })
  })

  it('shows PB panel with event types when events are present', async () => {
    // Arrange / Act
    renderDashboard()

    // Assert
    await waitFor(() => {
      expect(screen.getByTestId('pb-panel')).toBeInTheDocument()
      expect(within(screen.getByTestId('pb-panel')).getByText('MARATHON')).toBeInTheDocument()
    })
  })

  it('shows a needs-enrichment badge for events missing placement data', async () => {
    server.use(
      http.get('http://localhost/api/events', () =>
        HttpResponse.json({
          items: [
            {
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
            },
          ],
          nextCursor: null,
        })
      )
    )

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText(/needs enrichment/i)).toBeInTheDocument()
    })
  })

  it('does not show a needs-enrichment badge for complete events', async () => {
    renderDashboard()

    const heading = await screen.findByRole('heading', { name: /all events/i })
    const section = heading.closest('section')!

    await waitFor(() => {
      expect(within(section).getByText('Berlin Marathon')).toBeInTheDocument()
    })
    expect(within(section).queryByText(/needs enrichment/i)).not.toBeInTheDocument()
  })

  it('calls delete endpoint when delete button is clicked', async () => {
    let deletedId: string | undefined

    server.use(
      http.delete('http://localhost/api/events/:id', ({ params }) => {
        deletedId = params.id as string
        return new HttpResponse(null, { status: 204 })
      })
    )

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByLabelText('Delete event')).toBeInTheDocument()
    })

    await userEvent.click(screen.getByLabelText('Delete event'))

    await waitFor(() => {
      expect(deletedId).toBe('event-1')
    })
  })
})
