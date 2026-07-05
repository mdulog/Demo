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
  // NOTE: static paths must be registered before dynamic segments — MSW matches in order.
  // /personal-bests must precede /:id, and /:id must precede the bare /events catch-all.
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
  http.get('http://localhost/api/events/prediction', () =>
    HttpResponse.json({
      eventType: 'HYROX',
      predictedSecs: 4320,
      confidenceLabel: 'High',
      avgImprovementSecs: 215,
      dataPoints: [
        { eventId: 'event-1', eventDate: '2023-10-14', elapsedSecs: 4930, fittedSecs: 4920 },
        { eventId: 'event-2', eventDate: '2024-03-09', elapsedSecs: 4724, fittedSecs: 4710 },
        { eventId: 'event-3', eventDate: '2024-11-16', elapsedSecs: 4501, fittedSecs: 4508 },
        { eventId: null,      eventDate: '2026-04-25', elapsedSecs: null,  fittedSecs: 4320 },
      ],
    })
  ),
  http.get('http://localhost/api/events/timeline', () =>
    HttpResponse.json([
      {
        id: 'event-1',
        eventDate: '2024-09-29',
        eventType: 'MARATHON',
        elapsedSecs: 12600,
        completion: 'FINISHED',
      },
    ])
  ),
  http.get('http://localhost/api/events/:id', ({ params }) =>
    HttpResponse.json({
      id: params.id as string,
      eventType: 'MARATHON',
      eventName: 'Berlin Marathon',
      eventDate: '2024-09-29',
      completion: 'FINISHED',
      elapsedSecs: 14400,
      overallRank: 1500,
      ageGroupRank: null,
      fieldSize: 45000,
      ageGroupFieldSize: null,
      source: 'MANUAL',
      needsEnrichment: false,
      createdAt: '2024-10-01T00:00:00Z',
      splits: [
        { id: 'split-1', splitType: 'RUN', splitLabel: '10km', splitSecs: 2940, cumulativeSecs: 2940 },
        { id: 'split-2', splitType: 'RUN', splitLabel: '21km', splitSecs: 3180, cumulativeSecs: 6120 },
      ],
      averageSplits: [
        { label: '10km', avgSecs: 2940 },
        { label: '21km', avgSecs: 3180 },
      ],
    })
  ),
  http.get('http://localhost/api/events', () =>
    HttpResponse.json({
      items: [
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
          source: 'MANUAL',
          needsEnrichment: false,
          createdAt: '2024-10-01T00:00:00Z',
        },
      ],
      nextCursor: null,
    })
  ),
  http.delete('http://localhost/api/events/:id', () =>
    new HttpResponse(null, { status: 204 })
  ),
  http.post('http://localhost/api/events', () =>
    HttpResponse.json(
      {
        id: 'event-201',
        eventType: 'GENERIC',
        eventName: 'Local 10K',
        eventDate: '2026-05-01',
        completion: 'FINISHED',
        elapsedSecs: 2700,
        overallRank: null,
        ageGroupRank: null,
        fieldSize: null,
        ageGroupFieldSize: null,
        source: 'MANUAL',
        needsEnrichment: false,
        createdAt: '2026-05-01T00:00:00Z',
        splits: [],
      },
      { status: 201 }
    )
  ),
  http.post('http://localhost/api/events/upload', () =>
    HttpResponse.json(
      [
        {
          id: 'event-2',
          eventType: 'MARATHON',
          eventName: 'Test Half',
          eventDate: '2024-06-01',
          completion: 'FINISHED',
          elapsedSecs: 5400,
          overallRank: null,
          ageGroupRank: null,
          fieldSize: null,
          ageGroupFieldSize: null,
          source: 'CSV',
          needsEnrichment: false,
          createdAt: '2024-10-01T00:00:00Z',
          splits: [],
        },
      ],
      { status: 201 }
    )
  ),
  http.post('http://localhost/api/auth/logout', () =>
    new HttpResponse(null, { status: 204 })
  ),
  http.get('http://localhost/api/sync/strava/connect', () =>
    HttpResponse.json({ authorizeUrl: 'https://www.strava.com/oauth/authorize?client_id=test' })
  ),
  // Default: not connected — tests override this to exercise the connected states.
  http.get('http://localhost/api/sync/strava/activities', () =>
    new HttpResponse('No Strava connection found — connect Strava first.', { status: 409 })
  ),
  http.post('http://localhost/api/sync/strava/activities/confirm', () =>
    HttpResponse.json(
      {
        id: 'event-strava-1',
        eventType: 'GENERIC',
        eventName: 'Synced Activity',
        eventDate: '2026-05-10',
        completion: 'FINISHED',
        elapsedSecs: 4500,
        overallRank: null,
        ageGroupRank: null,
        fieldSize: null,
        ageGroupFieldSize: null,
        source: 'STRAVA',
        needsEnrichment: true,
        createdAt: '2026-05-10T00:00:00Z',
        splits: [],
      },
      { status: 201 }
    )
  ),
]

export const server = setupServer(...handlers)
