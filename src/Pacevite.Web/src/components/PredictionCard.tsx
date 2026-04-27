import type { PredictionResponse } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'

interface Props {
  prediction: PredictionResponse
}

const confidenceStyles: Record<string, string> = {
  High:   'bg-green-100 text-green-800',
  Medium: 'bg-yellow-100 text-yellow-800',
  Low:    'bg-red-100 text-red-800',
}

export function PredictionCard({ prediction }: Props) {
  const badgeClass = confidenceStyles[prediction.confidenceLabel] ?? confidenceStyles.Low

  return (
    <div className="bg-surface border border-border rounded-xl p-5 flex flex-col gap-3">
      <p className="text-xs font-semibold text-secondary uppercase tracking-widest">
        Next {prediction.eventType}
      </p>

      <p data-testid="prediction-time" className="text-4xl font-bold text-primary leading-none">
        {formatElapsed(prediction.predictedSecs)}
      </p>

      <div className="flex items-center gap-2">
        <span
          data-testid="confidence-badge"
          className={`text-xs font-semibold px-2 py-0.5 rounded-full ${badgeClass}`}
        >
          {prediction.confidenceLabel} confidence
        </span>
      </div>

      <div className="pt-3 border-t border-border grid grid-cols-2 gap-3">
        <div>
          <p className="text-xs text-secondary">Avg improvement</p>
          <p data-testid="avg-improvement" className="text-sm font-semibold text-green-600">
            ↓ {formatElapsed(prediction.avgImprovementSecs)} / race
          </p>
        </div>
      </div>
    </div>
  )
}
