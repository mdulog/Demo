import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts'
import type { DotItemDotProps } from 'recharts/types/util/types'
import type { TimelineEntry } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'
import { useTheme } from '@/context/ThemeContext'

interface Props {
  events: TimelineEntry[]
  pbId: string | undefined
}

interface DotEntry {
  id: string
}

function renderDot(pbId: string | undefined) {
  return function CustomDot({ cx, cy, payload }: DotItemDotProps & { payload: DotEntry }) {
    const x = cx ?? 0
    const y = cy ?? 0
    const isPb = payload.id === pbId
    return (
      <circle
        key={`dot-${payload.id}`}
        cx={x}
        cy={y}
        r={isPb ? 5 : 3}
        fill={isPb ? '#4ade80' : '#6366f1'}
      />
    )
  }
}

export function ProgressChart({ events, pbId }: Props) {
  useTheme() // re-render when theme changes so CSS vars are re-read

  const style = getComputedStyle(document.documentElement)
  const tickColor = style.getPropertyValue('--color-secondary').trim()
  const tooltipBg = style.getPropertyValue('--color-surface').trim()

  if (events.length === 0) {
    return (
      <p data-testid="progress-chart-empty" className="text-xs text-muted py-8 text-center">
        No events yet
      </p>
    )
  }

  const data = events.map(ev => ({ date: ev.eventDate, secs: ev.elapsedSecs, id: ev.id }))

  return (
    <div data-testid="progress-chart">
      <ResponsiveContainer width="100%" height={120}>
        <LineChart data={data} margin={{ top: 4, right: 4, bottom: 4, left: 40 }}>
          <XAxis dataKey="date" tick={{ fontSize: 10, fill: tickColor }} tickLine={false} />
          <YAxis
            tickFormatter={formatElapsed}
            tick={{ fontSize: 10, fill: tickColor }}
            tickLine={false}
            axisLine={false}
            reversed
            domain={['auto', 'auto']}
          />
          <Tooltip
            formatter={(value) => [formatElapsed(typeof value === 'number' ? value : 0), 'Time']}
            contentStyle={{ background: tooltipBg, border: 'none', fontSize: 12 }}
          />
          <Line
            type="monotone"
            dataKey="secs"
            stroke="#6366f1"
            strokeWidth={2}
            dot={renderDot(pbId)}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}
