// eslint-disable-next-line -- Cell is deprecated in Recharts v3 types but still functional
import { BarChart, Bar, XAxis, YAxis, Tooltip, Cell, ResponsiveContainer, LabelList } from 'recharts'
import type { SplitDelta } from '@/lib/chartUtils'
import { formatElapsed } from '@/lib/chartUtils'

interface Props {
  deltas: SplitDelta[]
}

export function SplitChart({ deltas }: Props) {
  if (deltas.length === 0) {
    return (
      <p data-testid="split-chart-empty" className="text-xs text-gray-400 py-8 text-center">
        No split data for this event
      </p>
    )
  }

  const data = deltas.map(d => ({
    label: d.label,
    secs: d.secs,
    faster: d.faster,
    deltaLabel: d.delta === 0 ? '—' : `${d.delta > 0 ? '+' : ''}${formatElapsed(Math.abs(d.delta))}`,
  }))

  return (
    <div data-testid="split-chart">
      <ResponsiveContainer width="100%" height={160}>
        <BarChart data={data} margin={{ top: 16, right: 8, bottom: 4, left: 40 }}>
          <XAxis dataKey="label" tick={{ fontSize: 10, fill: '#9ca3af' }} tickLine={false} />
          <YAxis
            tickFormatter={formatElapsed}
            tick={{ fontSize: 10, fill: '#9ca3af' }}
            tickLine={false}
            axisLine={false}
            domain={['auto', 'auto']}
          />
          <Tooltip
            formatter={(value) => [formatElapsed(typeof value === 'number' ? value : 0), 'Split time']}
            contentStyle={{ background: '#1f2937', border: 'none', fontSize: 12 }}
          />
          <Bar dataKey="secs" radius={[3, 3, 0, 0]}>
            {data.map((entry, index) => (
              <Cell key={`cell-${index}`} fill={entry.faster ? '#4ade80' : '#f87171'} />
            ))}
            <LabelList
              dataKey="deltaLabel"
              position="top"
              style={{ fontSize: 10, fill: '#6b7280' }}
            />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}
