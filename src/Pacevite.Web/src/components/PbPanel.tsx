import type { TimelineEntry } from '@/lib/types'
import { computePbs, groupByEventType, formatElapsed } from '@/lib/chartUtils'

interface Props {
  events: TimelineEntry[]
  selectedType: string
  onSelectType: (type: string) => void
}

export function PbPanel({ events, selectedType, onSelectType }: Props) {
  const pbs = computePbs(events)
  const grouped = groupByEventType(events)
  const eventTypes = Object.keys(pbs)

  if (eventTypes.length === 0) return null

  return (
    <div data-testid="pb-panel" className="space-y-2">
      {eventTypes.map(type => {
        const pb = pbs[type]
        const eventsOfType = grouped[type] ?? []
        const worst = Math.max(...eventsOfType.map(e => e.elapsedSecs))
        const best = pb.elapsedSecs
        const range = worst - best || 1
        const barPct = Math.round(((worst - best) / range) * 100)

        return (
          <button
            key={type}
            onClick={() => onSelectType(type)}
            className={`w-full flex items-center gap-3 text-left rounded px-2 py-1.5 transition-colors ${
              selectedType === type ? 'bg-badge' : 'hover:bg-surface'
            }`}
          >
            <span className="text-xs font-medium text-secondary w-28 truncate">{type}</span>
            <div className="flex-1 bg-border rounded-full h-1.5">
              <div
                className="bg-indigo-500 h-1.5 rounded-full"
                style={{ width: `${Math.max(barPct, 10)}%` }}
              />
            </div>
            <span className="text-xs font-semibold text-green-600 w-16 text-right">
              {formatElapsed(best)}
            </span>
          </button>
        )
      })}
    </div>
  )
}
