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

/**
 * Groups events by their eventType, with each group sorted by eventDate ascending.
 * Sorting by date within each group makes it straightforward to render progress-over-time charts
 * without requiring callers to re-sort.
 */
export function groupByEventType(events: EventResponse[]): Record<string, EventResponse[]> {
  const grouped: Record<string, EventResponse[]> = {}
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
export function computePbs(events: EventResponse[]): Record<string, EventResponse> {
  const pbs: Record<string, EventResponse> = {}
  for (const ev of events) {
    if (!pbs[ev.eventType] || ev.elapsedSecs < pbs[ev.eventType].elapsedSecs) {
      pbs[ev.eventType] = ev
    }
  }
  return pbs
}

/**
 * Computes the mean splitSecs for each distinct split label across all provided events.
 * Labels with no occurrences are never included — the result length equals the number of
 * distinct labels present in the input.
 */
export function computeAverageSplits(events: EventResponse[]): AverageSplit[] {
  const byLabel: Record<string, number[]> = {}
  for (const ev of events) {
    for (const split of ev.splits) {
      if (!byLabel[split.splitLabel]) byLabel[split.splitLabel] = []
      byLabel[split.splitLabel].push(split.splitSecs)
    }
  }
  return Object.entries(byLabel).map(([label, values]) => ({
    label,
    avgSecs: Math.round(values.reduce((sum, v) => sum + v, 0) / values.length),
  }))
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
