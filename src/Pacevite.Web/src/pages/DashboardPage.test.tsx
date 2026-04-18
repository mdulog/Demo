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
    // MARATHON appears in both sections (personal bests card + event list row)
    await waitFor(() => {
      expect(screen.getAllByText('MARATHON')).toHaveLength(2)
    })
  })

  it('shows empty state when no events exist', async () => {
    server.use(
      http.get('http://localhost/api/events', () => HttpResponse.json([])),
      http.get('http://localhost/api/events/personal-bests', () => HttpResponse.json([]))
    )

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('No events yet.')).toBeInTheDocument()
    })
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
