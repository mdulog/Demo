import type { EventResponse } from './types'

export interface AverageSplit {
  label: string
  avgSecs: number
}

export interface SplitDelta {
  label: string
  secs: number
  delta: number
  faster: boolean
}

// Minimal shape charts need — satisfied by both EventResponse and TimelineEntry.
export interface ChartEvent {
  id: string
  eventType: string
  eventDate: string
  elapsedSecs: number
}

/**
 * Groups events by their eventType, with each group sorted by eventDate ascending.
 * Sorting by date within each group makes it straightforward to render progress-over-time charts
 * without requiring callers to re-sort.
 */
export function groupByEventType<T extends ChartEvent>(events: T[]): Record<string, T[]> {
  const grouped: Record<string, T[]> = {}
  for (const ev of events) {
    if (!grouped[ev.eventType]) grouped[ev.eventType] = []
    grouped[ev.eventType].push(ev)
  }
  for (const key of Object.keys(grouped)) {
    grouped[key].sort((a, b) => a.eventDate.localeCompare(b.eventDate))
  }
  return grouped
}

/**
 * Returns the single best (lowest elapsed time) event per event type.
 * The PB event object is returned by reference — callers must not mutate it.
 */
export function computePbs<T extends ChartEvent>(events: T[]): Record<string, T> {
  const pbs: Record<string, T> = {}
  for (const ev of events) {
    if (!pbs[ev.eventType] || ev.elapsedSecs < pbs[ev.eventType].elapsedSecs) {
      pbs[ev.eventType] = ev
    }
  }
  return pbs
}

/**
 * For each split in an event, computes how far it deviates from the supplied average splits.
 * delta < 0 means the split was faster than average; delta > 0 means slower.
 * When no matching average exists for a label, delta defaults to 0.
 */
export function computeSplitDeltas(event: EventResponse, avgSplits: AverageSplit[]): SplitDelta[] {
  return event.splits.map(split => {
    const avg = avgSplits.find(a => a.label === split.splitLabel)
    const delta = avg ? split.splitSecs - avg.avgSecs : 0
    return { label: split.splitLabel, secs: split.splitSecs, delta, faster: delta < 0 }
  })
}

/**
 * Formats a duration in seconds as a human-readable string.
 * Sub-hour durations use "m:ss"; durations of one hour or more use "h:mm:ss".
 */
export function formatElapsed(secs: number): string {
  const h = Math.floor(secs / 3600)
  const m = Math.floor((secs % 3600) / 60)
  const s = secs % 60
  if (h > 0) {
    return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  }
  return `${m}:${String(s).padStart(2, '0')}`
}
