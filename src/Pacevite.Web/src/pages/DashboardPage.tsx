import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api'
import { useAuth } from '@/hooks/useAuth'
import { useEvents } from '@/hooks/useEvents'
import { useTimeline } from '@/hooks/useTimeline'
import { formatTime, type PersonalBestResponse } from '@/lib/types'
import { groupByEventType, computePbs } from '@/lib/chartUtils'
import { ProgressChart } from '@/components/ProgressChart'
import { PbPanel } from '@/components/PbPanel'
import { PredictionTeaser } from '@/components/PredictionTeaser'
import { ThemeToggle } from '@/components/ThemeToggle'
import { Upload, Plus, Trash2, Trophy, LogOut, ChartLine, ChartNoAxesColumn, Link2, Search } from 'lucide-react'

const SEARCH_DEBOUNCE_MS = 300

export function DashboardPage() {
  const { user, logout } = useAuth()
  const queryClient = useQueryClient()

  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  const { data, isLoading: eventsLoading, fetchNextPage, hasNextPage, isFetchingNextPage } =
    useEvents({ search: debouncedSearch || undefined })
  const events = data?.pages.flatMap(p => p.items) ?? []

  const { data: timeline = [] } = useTimeline()
  const grouped = groupByEventType(timeline)
  const pbs = computePbs(timeline)
  const defaultType = Object.keys(grouped)[0] ?? ''
  const [selectedType, setSelectedType] = useState<string>(defaultType)
  const chartType = selectedType || defaultType
  const chartEvents = grouped[chartType] ?? []
  const pbId = pbs[chartType]?.id

  const { data: personalBests = [] } = useQuery({
    queryKey: ['personal-bests'],
    queryFn: async () => {
      const { data } = await apiClient.get<PersonalBestResponse[]>('/events/personal-bests')
      return data
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => apiClient.delete(`/events/${id}`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['events'] })
      void queryClient.invalidateQueries({ queryKey: ['personal-bests'] })
      void queryClient.invalidateQueries({ queryKey: ['timeline'] })
    },
  })

  return (
    <div className="min-h-screen bg-bg">
      {/* Nav */}
      <header className="bg-surface border-b border-border px-6 py-4 flex items-center justify-between">
        <h1 className="text-lg font-semibold text-primary">Pacevite</h1>
        <div className="flex items-center gap-4">
          <span className="text-sm text-secondary">{user?.email}</span>
          <Link
            to="/events/new"
            className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
          >
            <Plus size={14} /> Add Event
          </Link>
          <Link
            to="/upload"
            className="inline-flex items-center gap-2 bg-action text-action-fg text-sm px-3 py-2 rounded-md hover:bg-action-hover"
          >
            <Upload size={14} /> Upload
          </Link>
          <Link
            to="/predict"
            className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
          >
            <ChartNoAxesColumn size={14} /> Predict
          </Link>
          <Link
            to="/sync"
            className="inline-flex items-center gap-2 text-sm text-secondary hover:text-primary"
          >
            <Link2 size={14} /> Sync
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

      <main className="max-w-5xl mx-auto px-6 py-8 space-y-8">
        <PredictionTeaser />
        {/* Analytics panels */}
        {timeline.length > 0 && (
          <section data-testid="progress-chart-panel">
            <div className="flex items-center justify-between mb-3">
              <h2 className="text-sm font-semibold text-secondary uppercase tracking-wide flex items-center gap-2">
                <ChartLine size={14} /> Progress
              </h2>
              {Object.keys(grouped).length > 1 && (
                <select
                  value={chartType}
                  onChange={e => setSelectedType(e.target.value)}
                  className="text-xs border border-border rounded px-2 py-1 text-secondary bg-surface"
                >
                  {Object.keys(grouped).map(t => (
                    <option key={t} value={t}>{t}</option>
                  ))}
                </select>
              )}
            </div>
            <div className="bg-surface rounded-lg border border-border p-4 grid grid-cols-1 lg:grid-cols-2 gap-4">
              <ProgressChart events={chartEvents} pbId={pbId} />
              <PbPanel events={timeline} selectedType={chartType} onSelectType={setSelectedType} />
            </div>
          </section>
        )}

        {timeline.length === 0 && !eventsLoading && (
          <div data-testid="progress-chart-empty" className="hidden" />
        )}

        {/* Personal Bests */}
        {personalBests.length > 0 && (
          <section>
            <h2 className="text-sm font-semibold text-secondary uppercase tracking-wide mb-3 flex items-center gap-2">
              <Trophy size={14} /> Personal Bests
            </h2>
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3">
              {personalBests.map(pb => (
                <div key={pb.eventId} className="bg-surface rounded-lg border border-border p-4">
                  <p className="text-xs font-medium text-secondary uppercase">{pb.eventType}</p>
                  <p className="text-2xl font-bold text-primary mt-1">{formatTime(pb.elapsedSecs)}</p>
                  <p className="text-xs text-secondary mt-1 truncate">{pb.eventName}</p>
                  <p className="text-xs text-muted">{pb.eventDate}</p>
                </div>
              ))}
            </div>
          </section>
        )}

        {/* Event List */}
        <section>
          <h2 className="text-sm font-semibold text-secondary uppercase tracking-wide mb-3">
            All Events
          </h2>

          <div className="relative mb-3">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
            <input
              type="search"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search events…"
              className="w-full bg-surface border border-border rounded-md pl-9 pr-3 py-2 text-sm text-primary placeholder:text-muted"
            />
          </div>

          {eventsLoading && <p className="text-sm text-secondary">Loading…</p>}

          {!eventsLoading && events.length === 0 && !debouncedSearch && (
            <div className="bg-surface rounded-lg border border-dashed border-border p-12 text-center">
              <p className="text-secondary text-sm">No events yet.</p>
              <Link to="/upload" className="text-sm font-medium text-primary underline mt-2 inline-block">
                Upload your first event
              </Link>
            </div>
          )}

          {!eventsLoading && events.length === 0 && debouncedSearch && (
            <p className="text-sm text-secondary">No events match “{debouncedSearch}”.</p>
          )}

          {events.length > 0 && (
            <div className="bg-surface rounded-lg border border-border divide-y divide-border">
              {events.map(ev => (
                <div key={ev.id} className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-4">
                    <span className="text-xs font-medium bg-badge text-badge-fg px-2 py-0.5 rounded uppercase">
                      {ev.eventType}
                    </span>
                    <div>
                      <p className="text-sm font-medium text-primary">{ev.eventName}</p>
                      <p className="text-xs text-secondary">{ev.eventDate}</p>
                    </div>
                    {ev.needsEnrichment && (
                      <span className="text-xs font-medium bg-amber-100 text-amber-800 px-2 py-0.5 rounded">
                        Needs enrichment
                      </span>
                    )}
                  </div>
                  <div className="flex items-center gap-6">
                    <div className="text-right">
                      <p className="text-sm font-semibold text-primary">{formatTime(ev.elapsedSecs)}</p>
                      <p className={`text-xs ${ev.completion === 'FINISHED' ? 'text-green-600' : 'text-red-500'}`}>
                        {ev.completion}
                      </p>
                    </div>
                    {ev.overallRank && (
                      <p className="text-xs text-secondary w-20 text-right">
                        #{ev.overallRank}{ev.fieldSize ? ` / ${ev.fieldSize}` : ''}
                      </p>
                    )}
                    <Link
                      to={`/events/${ev.id}`}
                      className="text-xs text-indigo-600 hover:text-indigo-800"
                    >
                      View
                    </Link>
                    <button
                      onClick={() => deleteMutation.mutate(ev.id)}
                      disabled={deleteMutation.isPending}
                      className="text-muted hover:text-red-500 disabled:opacity-40"
                      aria-label="Delete event"
                    >
                      <Trash2 size={14} />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          {hasNextPage && (
            <div className="mt-3 text-center">
              <button
                onClick={() => fetchNextPage()}
                disabled={isFetchingNextPage}
                className="text-sm font-medium text-indigo-600 hover:text-indigo-800 disabled:opacity-40"
              >
                {isFetchingNextPage ? 'Loading…' : 'Load more'}
              </button>
            </div>
          )}
        </section>
      </main>
    </div>
  )
}
