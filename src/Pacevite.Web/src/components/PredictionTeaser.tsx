import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { useTimeline } from '@/hooks/useTimeline'
import { usePrediction } from '@/hooks/usePrediction'
import { formatElapsed } from '@/lib/chartUtils'
import { TrendingDown } from 'lucide-react'

export function PredictionTeaser() {
  const { data: timeline = [], isLoading: eventsLoading } = useTimeline()

  const mostRecentType = useMemo(() => {
    const sorted = [...timeline].sort((a, b) => b.eventDate.localeCompare(a.eventDate))
    return sorted[0]?.eventType ?? null
  }, [timeline])

  const { data: prediction, isLoading: predLoading, isError } = usePrediction(mostRecentType)

  if (eventsLoading || predLoading || isError || !prediction) return null

  return (
    <div
      data-testid="prediction-teaser"
      className="bg-surface border border-border rounded-xl p-4 flex items-center justify-between"
    >
      <div>
        <p className="text-xs font-semibold text-secondary uppercase tracking-widest">
          Predicted next {prediction.eventType}
        </p>
        <p className="text-2xl font-bold text-primary mt-0.5">
          {formatElapsed(prediction.predictedSecs)}
        </p>
        <p className="text-xs text-green-600 mt-0.5 flex items-center gap-1">
          <TrendingDown size={11} />
          ↓ {formatElapsed(prediction.avgImprovementSecs)} avg / race
        </p>
      </div>
      <Link
        data-testid="teaser-link"
        to="/predict"
        className="text-sm font-medium text-indigo-600 hover:text-indigo-800"
      >
        Full analysis →
      </Link>
    </div>
  )
}
