import { describe, it, expect } from 'vitest'
import {
  groupByEventType,
  computePbs,
  computeAverageSplits,
  computeSplitDeltas,
  formatElapsed,
  type AverageSplit,
} from './chartUtils'
import type { EventResponse } from './types'

const makeEvent = (overrides: Partial<EventResponse> = {}): EventResponse => ({
  id: 'event-1',
  eventType: 'MARATHON',
  eventName: 'Test Marathon',
  eventDate: '2024-01-01',
  completion: 'FINISHED',
  elapsedSecs: 14400,
  overallRank: null,
  ageGroupRank: null,
  fieldSize: null,
  ageGroupFieldSize: null,
  source: 'manual',
  needsEnrichment: false,
  createdAt: '2024-01-01T00:00:00Z',
  splits: [],
  ...overrides,
})

describe('groupByEventType', () => {
  it('returns empty object for empty input', () => {
    // Arrange
    const events: EventResponse[] = []

    // Act
    const result = groupByEventType(events)

    // Assert
    expect(result).toEqual({})
  })

  it('groups events by eventType', () => {
    // Arrange
    const events = [
      makeEvent({ id: 'e1', eventType: 'MARATHON' }),
      makeEvent({ id: 'e2', eventType: '10K' }),
      makeEvent({ id: 'e3', eventType: 'MARATHON' }),
    ]

    // Act
    const result = groupByEventType(events)

    // Assert
    expect(Object.keys(result)).toHaveLength(2)
    expect(result['MARATHON']).toHaveLength(2)
    expect(result['10K']).toHaveLength(1)
  })

  it('sorts events within each group by eventDate ascending', () => {
    // Arrange
    const events = [
      makeEvent({ id: 'e1', eventDate: '2024-09-01' }),
      makeEvent({ id: 'e2', eventDate: '2024-03-01' }),
      makeEvent({ id: 'e3', eventDate: '2024-06-01' }),
    ]

    // Act
    const result = groupByEventType(events)

    // Assert
    const dates = result['MARATHON'].map(e => e.eventDate)
    expect(dates).toEqual(['2024-03-01', '2024-06-01', '2024-09-01'])
  })
})

describe('computePbs', () => {
  it('returns empty object for empty input', () => {
    // Arrange
    const events: EventResponse[] = []

    // Act
    const result = computePbs(events)

    // Assert
    expect(result).toEqual({})
  })

  it('returns the event with the lowest elapsedSecs per type', () => {
    // Arrange
    const events = [
      makeEvent({ id: 'e1', eventType: 'MARATHON', elapsedSecs: 14400 }),
      makeEvent({ id: 'e2', eventType: 'MARATHON', elapsedSecs: 13500 }),
      makeEvent({ id: 'e3', eventType: 'MARATHON', elapsedSecs: 15000 }),
    ]

    // Act
    const result = computePbs(events)

    // Assert
    expect(result['MARATHON'].id).toBe('e2')
    expect(result['MARATHON'].elapsedSecs).toBe(13500)
  })

  it('tracks PBs independently per event type', () => {
    // Arrange
    const events = [
      makeEvent({ id: 'e1', eventType: 'MARATHON', elapsedSecs: 14400 }),
      makeEvent({ id: 'e2', eventType: '10K', elapsedSecs: 2900 }),
    ]

    // Act
    const result = computePbs(events)

    // Assert
    expect(result['MARATHON'].id).toBe('e1')
    expect(result['10K'].id).toBe('e2')
  })

  it('keeps the first seen event when two events have equal elapsedSecs', () => {
    // Arrange
    const events = [
      makeEvent({ id: 'first', elapsedSecs: 14400 }),
      makeEvent({ id: 'second', elapsedSecs: 14400 }),
    ]

    // Act
    const result = computePbs(events)

    // Assert — strict < means first-seen wins on tie
    expect(result['MARATHON'].id).toBe('first')
  })
})

describe('computeAverageSplits', () => {
  it('returns empty array for events with no splits', () => {
    // Arrange
    const events = [makeEvent()]

    // Act
    const result = computeAverageSplits(events)

    // Assert
    expect(result).toEqual([])
  })

  it('returns empty array for empty input', () => {
    // Arrange
    const events: EventResponse[] = []

    // Act
    const result = computeAverageSplits(events)

    // Assert
    expect(result).toEqual([])
  })

  it('computes mean splitSecs per split label', () => {
    // Arrange
    const events = [
      makeEvent({
        splits: [
          { id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 3000, cumulativeSecs: 3000 },
        ],
      }),
      makeEvent({
        splits: [
          { id: 's2', splitType: 'RUN', splitLabel: '10km', splitSecs: 3600, cumulativeSecs: 3600 },
        ],
      }),
    ]

    // Act
    const result = computeAverageSplits(events)

    // Assert
    expect(result).toHaveLength(1)
    expect(result[0].label).toBe('10km')
    expect(result[0].avgSecs).toBe(3300)
  })

  it('handles multiple distinct split labels', () => {
    // Arrange
    const events = [
      makeEvent({
        splits: [
          { id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 3000, cumulativeSecs: 3000 },
          { id: 's2', splitType: 'RUN', splitLabel: '21km', splitSecs: 3300, cumulativeSecs: 6300 },
        ],
      }),
    ]

    // Act
    const result = computeAverageSplits(events)

    // Assert
    expect(result).toHaveLength(2)
    expect(result.find(s => s.label === '10km')!.avgSecs).toBe(3000)
    expect(result.find(s => s.label === '21km')!.avgSecs).toBe(3300)
  })
})

describe('computeSplitDeltas', () => {
  it('returns faster=true when split is below average', () => {
    // Arrange
    const event = makeEvent({
      splits: [{ id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 2800, cumulativeSecs: 2800 }],
    })
    const avgSplits: AverageSplit[] = [{ label: '10km', avgSecs: 3000 }]

    // Act
    const result = computeSplitDeltas(event, avgSplits)

    // Assert
    expect(result[0].delta).toBe(-200)
    expect(result[0].faster).toBe(true)
  })

  it('returns faster=false when split is above average', () => {
    // Arrange
    const event = makeEvent({
      splits: [{ id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 3200, cumulativeSecs: 3200 }],
    })
    const avgSplits: AverageSplit[] = [{ label: '10km', avgSecs: 3000 }]

    // Act
    const result = computeSplitDeltas(event, avgSplits)

    // Assert
    expect(result[0].delta).toBe(200)
    expect(result[0].faster).toBe(false)
  })

  it('returns delta=0 and faster=false when no matching average split exists', () => {
    // Arrange
    const event = makeEvent({
      splits: [{ id: 's1', splitType: 'RUN', splitLabel: 'Unknown', splitSecs: 3000, cumulativeSecs: 3000 }],
    })

    // Act
    const result = computeSplitDeltas(event, [])

    // Assert — no comparison baseline: delta is 0, faster defaults to false
    expect(result[0].delta).toBe(0)
    expect(result[0].faster).toBe(false)
  })

  it('returns empty array for event with no splits', () => {
    // Arrange
    const event = makeEvent()

    // Act
    const result = computeSplitDeltas(event, [])

    // Assert
    expect(result).toEqual([])
  })

  it('returns delta=0 and faster=false when split matches average exactly', () => {
    // Arrange
    const event = makeEvent({
      splits: [{ id: 's1', splitType: 'RUN', splitLabel: '10km', splitSecs: 3000, cumulativeSecs: 3000 }],
    })
    const avgSplits: AverageSplit[] = [{ label: '10km', avgSecs: 3000 }]

    // Act
    const result = computeSplitDeltas(event, avgSplits)

    // Assert
    expect(result[0].delta).toBe(0)
    expect(result[0].faster).toBe(false)
  })
})

describe('formatElapsed', () => {
  it('formats zero seconds as 0:00', () => {
    // Arrange / Act / Assert
    expect(formatElapsed(0)).toBe('0:00')
  })

  it('formats sub-hour as m:ss', () => {
    // Arrange / Act / Assert
    expect(formatElapsed(330)).toBe('5:30')
  })

  it('formats exactly one hour as 1:00:00', () => {
    // Arrange / Act / Assert
    expect(formatElapsed(3600)).toBe('1:00:00')
  })

  it('formats multi-hour correctly', () => {
    // Arrange / Act / Assert
    expect(formatElapsed(14523)).toBe('4:02:03')
  })

  it('pads minutes and seconds', () => {
    // Arrange / Act / Assert
    expect(formatElapsed(65)).toBe('1:05')
  })
})
