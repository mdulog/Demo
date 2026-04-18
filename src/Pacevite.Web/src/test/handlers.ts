import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

export const handlers = [
  http.post('http://localhost/api/auth/register', () =>
    HttpResponse.json(
      { userId: 'user-1', email: 'test@example.com', token: 'token-abc' },
      { status: 201 }
    )
  ),
  http.post('http://localhost/api/auth/login', () =>
    HttpResponse.json({ userId: 'user-1', email: 'test@example.com', token: 'token-abc' })
  ),
  http.get('http://localhost/api/events', () =>
    HttpResponse.json([
      {
        id: 'event-1',
        eventType: 'MARATHON',
        eventName: 'Berlin Marathon',
        eventDate: '2024-09-29',
        completion: 'FINISHED',
        elapsedSecs: 12600,
        overallRank: 1500,
        ageGroupRank: null,
        fieldSize: 45000,
        ageGroupFieldSize: null,
        source: 'manual',
        createdAt: '2024-10-01T00:00:00Z',
        splits: [],
      },
    ])
  ),
  http.get('http://localhost/api/events/personal-bests', () =>
    HttpResponse.json([
      {
        eventType: 'MARATHON',
        eventId: 'event-1',
        eventName: 'Berlin Marathon',
        eventDate: '2024-09-29',
        elapsedSecs: 12600,
      },
    ])
  ),
  http.delete('http://localhost/api/events/:id', () =>
    new HttpResponse(null, { status: 204 })
  ),
  http.post('http://localhost/api/events/upload', () =>
    HttpResponse.json(
      [
        {
          id: 'event-2',
          eventType: 'HALF_MARATHON',
          eventName: 'Test Half',
          eventDate: '2024-06-01',
          completion: 'FINISHED',
          elapsedSecs: 5400,
          overallRank: null,
          ageGroupRank: null,
          fieldSize: null,
          ageGroupFieldSize: null,
          source: 'csv',
          createdAt: '2024-10-01T00:00:00Z',
          splits: [],
        },
      ],
      { status: 201 }
    )
  ),
]

export const server = setupServer(...handlers)
