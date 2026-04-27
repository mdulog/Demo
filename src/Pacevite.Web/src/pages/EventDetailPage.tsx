import { Link, useParams } from 'react-router-dom'
import { useEvent } from '@/hooks/useEvent'
import { useEvents } from '@/hooks/useEvents'
import { computeAverageSplits, computeSplitDeltas, formatElapsed } from '@/lib/chartUtils'
import { SplitChart } from '@/components/SplitChart'
import { RaceComparison } from '@/components/RaceComparison'
import { ThemeToggle } from '@/components/ThemeToggle'
import { ChartNoAxesColumn } from 'lucide-react'

export function EventDetailPage() {
  const { id } = useParams<{ id: string }>()

  const { data: event, isLoading } = useEvent(id)
  const { data: allEvents = [] } = useEvents()

  if (isLoading) return <p className="p-8 text-secondary">Loading…</p>
  if (!event) return <p className="p-8 text-secondary">Event not found.</p>

  const sameTypeEvents = allEvents.filter(e => e.eventType === event.eventType)
  const avgSplits = computeAverageSplits(sameTypeEvents)
  const splitDeltas = computeSplitDeltas(event, avgSplits)

  return (
    <div className="min-h-screen bg-bg">
      <header className="bg-surface border-b border-border px-6 py-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-primary">Pacevite</h1>
        <div className="flex items-center gap-4">
          <Link
            to="/predict"
            className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
          >
            <ChartNoAxesColumn size={14} /> Predict
          </Link>
          <ThemeToggle />
          <Link to="/dashboard" className="text-sm text-indigo-600 hover:text-indigo-800">
            ← Dashboard
          </Link>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-6 py-8 space-y-6">
        <div>
          <p className="text-xs uppercase tracking-widest text-muted mb-1">
            {event.eventType} · {event.eventDate}
          </p>
          <h2 className="text-2xl font-bold text-primary">{event.eventName}</h2>
          <div className="flex gap-6 mt-2 text-sm text-secondary">
            <span>Time: <strong className="text-primary">{formatElapsed(event.elapsedSecs)}</strong></span>
            {event.overallRank != null && event.fieldSize != null && (
              <span>Overall: <strong className="text-primary">#{event.overallRank} / {event.fieldSize}</strong></span>
            )}
            {event.ageGroupRank != null && event.ageGroupFieldSize != null && (
              <span>Age group: <strong className="text-primary">#{event.ageGroupRank} / {event.ageGroupFieldSize}</strong></span>
            )}
          </div>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div className="bg-surface rounded-lg border border-border p-4">
            <h3 className="text-xs font-semibold text-secondary uppercase tracking-wide mb-3">
              Split Breakdown
            </h3>
            <SplitChart deltas={splitDeltas} />
          </div>

          <div className="bg-surface rounded-lg border border-border p-4">
            <h3 className="text-xs font-semibold text-secondary uppercase tracking-wide mb-3">
              vs. Your {event.eventType} Average
            </h3>
            <RaceComparison event={event} sameTypeEvents={sameTypeEvents} />
          </div>
        </div>
      </main>
    </div>
  )
}
