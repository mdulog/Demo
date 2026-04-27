import { ComposedChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts'
import type { PredictionDataPoint } from '@/lib/types'
import { formatElapsed } from '@/lib/chartUtils'
import { useTheme } from '@/context/ThemeContext'

interface Props {
  dataPoints: PredictionDataPoint[]
}

export function PredictionChart({ dataPoints }: Props) {
  useTheme()

  const style     = getComputedStyle(document.documentElement)
  const tickColor = style.getPropertyValue('--color-secondary').trim()
  const tooltipBg = style.getPropertyValue('--color-surface').trim()

  const historical = dataPoints.filter(p => p.eventId !== null)

  if (historical.length === 0) {
    return (
      <p data-testid="prediction-chart-empty" className="text-xs text-muted py-8 text-center">
        No data
      </p>
    )
  }

  const data = dataPoints.map(p => ({
    date:        p.eventDate,
    actual:      p.elapsedSecs ?? undefined,
    fitted:      p.fittedSecs,
    isProjected: p.eventId === null,
  }))

  return (
    <div data-testid="prediction-chart">
      <ResponsiveContainer width="100%" height={160}>
        <ComposedChart data={data} margin={{ top: 4, right: 4, bottom: 4, left: 40 }}>
          <XAxis dataKey="date" tick={{ fontSize: 10, fill: tickColor }} tickLine={false} />
          <YAxis
            tickFormatter={v => formatElapsed(typeof v === 'number' ? v : 0)}
            tick={{ fontSize: 10, fill: tickColor }}
            tickLine={false}
            axisLine={false}
            reversed
            domain={['auto', 'auto']}
          />
          <Tooltip
            formatter={(v) => [formatElapsed(typeof v === 'number' ? v : 0), '']}
            contentStyle={{ background: tooltipBg, border: 'none', fontSize: 12 }}
          />
          <Line
            type="monotone"
            dataKey="fitted"
            stroke="#6366f1"
            strokeWidth={2}
            strokeDasharray="6 4"
            dot={false}
            opacity={0.6}
          />
          <Line
            type="monotone"
            dataKey="actual"
            stroke="#6366f1"
            strokeWidth={2}
            connectNulls={false}
            dot={({ cx, cy }) => (
              <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r={4} fill="#6366f1" />
            )}
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  )
}
