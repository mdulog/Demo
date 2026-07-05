import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '@/hooks/useAuth'
import { useTimeline } from '@/hooks/useTimeline'
import { usePrediction } from '@/hooks/usePrediction'
import { ThemeToggle } from '@/components/ThemeToggle'
import { PredictionCard } from '@/components/PredictionCard'
import { PredictionChart } from '@/components/PredictionChart'
import { PredictionCoaching } from '@/components/PredictionCoaching'
import { Upload, LogOut, ChartNoAxesColumn } from 'lucide-react'

export function PredictPage() {
  const { user, logout } = useAuth()
  const { data: events = [], isLoading: eventsLoading } = useTimeline()

  const eligibleTypes = useMemo(() => {
    const counts: Record<string, number> = {}
    for (const ev of events) {
      if (ev.completion === 'FINISHED') {
        counts[ev.eventType] = (counts[ev.eventType] ?? 0) + 1
      }
    }
    return Object.entries(counts)
      .filter(([, n]) => n >= 2)
      .map(([type]) => type)
  }, [events])

  const [selectedType, setSelectedType] = useState<string | null>(null)
  const activeType = selectedType ?? eligibleTypes[0] ?? null

  const { data: prediction, isLoading: predLoading, isError } = usePrediction(activeType)

  return (
    <div className="min-h-screen bg-bg">
      <header className="bg-surface border-b border-border px-6 py-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-primary">Pacevite</h1>
        <div className="flex items-center gap-4">
          <span className="text-sm text-secondary">{user?.email}</span>
          <Link
            to="/upload"
            className="inline-flex items-center gap-2 bg-action text-action-fg text-sm px-3 py-2 rounded-md hover:bg-action-hover"
          >
            <Upload size={14} /> Upload
          </Link>
          <Link
            to="/predict"
            className="inline-flex items-center gap-2 text-sm font-medium text-indigo-600 hover:text-indigo-800"
          >
            <ChartNoAxesColumn size={14} /> Predict
          </Link>
          <ThemeToggle />
          <button
            onClick={logout}
            className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
          >
            <LogOut size={14} /> Sign out
          </button>
        </div>
      </header>

      <main className="max-w-5xl mx-auto px-6 py-8 space-y-6">
        <div>
          <h2 className="text-xl font-semibold text-primary mb-1">Performance Prediction</h2>
          <p className="text-sm text-secondary">
            Based on your finished events. Select an event type to see your trajectory.
          </p>
        </div>

        {eventsLoading && <p className="text-sm text-secondary">Loading…</p>}

        {!eventsLoading && eligibleTypes.length === 0 && (
          <div className="bg-surface border border-dashed border-border rounded-xl p-12 text-center">
            <p className="text-secondary text-sm">
              You need at least 2 finished events of the same type to generate a prediction.
            </p>
            <Link to="/upload" className="text-sm font-medium text-primary underline mt-2 inline-block">
              Upload events
            </Link>
          </div>
        )}

        {eligibleTypes.length > 0 && (
          <>
            <div className="flex gap-2 flex-wrap">
              {eligibleTypes.map(type => (
                <button
                  key={type}
                  onClick={() => setSelectedType(type)}
                  className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                    activeType === type
                      ? 'bg-action text-action-fg'
                      : 'bg-badge text-badge-fg hover:bg-border'
                  }`}
                >
                  {type}
                </button>
              ))}
            </div>

            {predLoading && <p className="text-sm text-secondary">Calculating prediction…</p>}

            {isError && (
              <p className="text-sm text-red-500">
                Not enough data to predict for {activeType}. Upload more events.
              </p>
            )}

            {prediction && !predLoading && (
              <div className="grid grid-cols-1 lg:grid-cols-[280px_1fr] gap-6 items-start">
                <PredictionCard prediction={prediction} />
                <div className="bg-surface border border-border rounded-xl p-5">
                  <p className="text-sm font-medium text-primary mb-3">Trend</p>
                  <PredictionChart dataPoints={prediction.dataPoints} />
                </div>
              </div>
            )}

            {prediction && (
              <PredictionCoaching eventType={activeType ?? ''} />
            )}
          </>
        )}
      </main>
    </div>
  )
}
