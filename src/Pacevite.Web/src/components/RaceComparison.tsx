import { LineChart, Line, Tooltip, ResponsiveContainer } from 'recharts'
import type { EventResponse, TimelineEntry } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'
import { useTheme } from '@/context/ThemeContext'

interface Props {
  event: EventResponse
  sameTypeEvents: TimelineEntry[]
}

export function RaceComparison({ event, sameTypeEvents }: Props) {
  useTheme() // re-render when theme changes so CSS vars are re-read

  const style = getComputedStyle(document.documentElement)
  const tooltipBg = style.getPropertyValue('--color-surface').trim()
  const sparkStroke = style.getPropertyValue('--color-secondary').trim()
  const dotFill = style.getPropertyValue('--color-muted').trim()

  const sorted = [...sameTypeEvents].sort((a, b) => a.eventDate.localeCompare(b.eventDate))
  const avg = sorted.length > 0
    ? Math.round(sorted.reduce((s, e) => s + e.elapsedSecs, 0) / sorted.length)
    : event.elapsedSecs
  const delta = event.elapsedSecs - avg
  const best = sorted.length > 0 ? Math.min(...sorted.map(e => e.elapsedSecs)) : event.elapsedSecs
  const worst = sorted.length > 0 ? Math.max(...sorted.map(e => e.elapsedSecs)) : event.elapsedSecs
  const sparkData = sorted.map(e => ({ date: e.eventDate, secs: e.elapsedSecs, id: e.id }))

  return (
    <div data-testid="race-comparison" className="space-y-3">
      {sorted.length < 2 ? (
        <p className="text-xs text-muted py-4 text-center">
          Race more {event.eventType} events to see your trend
        </p>
      ) : (
        <>
          <div className="text-center">
            <p className={`text-3xl font-bold ${delta < 0 ? 'text-green-600' : 'text-red-500'}`}>
              {delta < 0 ? '−' : '+'}{formatElapsed(Math.abs(delta))}
            </p>
            <p className="text-xs text-secondary mt-1">
              {delta < 0 ? 'faster' : 'slower'} than your avg ({formatElapsed(avg)})
            </p>
          </div>

          <ResponsiveContainer width="100%" height={60}>
            <LineChart data={sparkData} margin={{ top: 4, right: 4, bottom: 4, left: 4 }}>
              <Tooltip
                formatter={(value) => [formatElapsed(typeof value === 'number' ? value : 0), 'Time']}
                contentStyle={{ background: tooltipBg, border: 'none', fontSize: 11 }}
              />
              <Line
                type="monotone"
                dataKey="secs"
                stroke={sparkStroke}
                strokeWidth={1.5}
                dot={({ cx = 0, cy = 0, payload }: { cx?: number; cy?: number; payload: { id: string } }) => (
                  <circle
                    key={`spark-dot-${payload.id}`}
                    cx={cx}
                    cy={cy}
                    r={payload.id === event.id ? 5 : 3}
                    fill={payload.id === event.id ? '#4ade80' : dotFill}
                  />
                )}
              />
            </LineChart>
          </ResponsiveContainer>

          <div className="flex justify-between text-xs text-secondary pt-1 border-t border-border">
            <span>Best <strong className="text-green-600">{formatElapsed(best)}</strong></span>
            <span>Worst <strong className="text-secondary">{formatElapsed(worst)}</strong></span>
          </div>
        </>
      )}
    </div>
  )
}
